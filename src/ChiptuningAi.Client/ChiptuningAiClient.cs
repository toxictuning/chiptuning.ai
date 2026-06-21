using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChiptuningAi.Client.Auth;
using ChiptuningAi.Client.BulkImport;
using ChiptuningAi.Client.Common;
using ChiptuningAi.Client.Files;
using ChiptuningAi.Client.Lookups;
using ChiptuningAi.Client.Patches;

namespace ChiptuningAi.Client;

/// <summary>
/// Main entry point for the Chiptuning.Ai API.
/// </summary>
/// <example>
/// <code>
/// var client = new ChiptuningAiClient();
/// await client.Auth.LoginAsync("you@example.com", "password");
///
/// var file = await client.Files.UploadAsync("ecu.bin", new FileMetadata
/// {
///     VehicleClass   = "Passenger Car",
///     VehicleMake    = "BMW",
///     VehicleModel   = "3 Series",
///     VehicleVariant = "320i",
///     EngineType     = "Diesel",
///     ECUType        = "ECU",
///     ECUMake        = "Bosch",
///     ECUModel       = "EDC17C64",
///     ReadHardware   = "CMD Flash",
///     ReadMode       = "OBD",
/// });
///
/// var patch = await client.Patches.UploadAsync("ecu_tuned.bin", file.FileId, description: "Stage 1");
/// var result = await client.Patches.ApplyAsync(patch.PatchId, file.FileId);
/// </code>
/// </example>
public sealed class ChiptuningAiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private string? _accessToken;

    /// <summary>Current access token, or null if not authenticated.</summary>
    public string? AccessToken => _accessToken;

    /// <summary>Current refresh token, or null if not authenticated.</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>UTC time when the current access token expires, or null if unknown.</summary>
    public DateTimeOffset? TokenExpiresAt { get; internal set; }

    // ── Sub-clients ───────────────────────────────────────────────────────────

    /// <summary>Authentication operations — login, refresh, logout, profile.</summary>
    public AuthClient Auth { get; }

    /// <summary>File upload and management operations.</summary>
    public FilesClient Files { get; }

    /// <summary>Patch creation, application, and history operations.</summary>
    public PatchesClient Patches { get; }

    /// <summary>Autocomplete lookup values — vehicle classes, ECU makes/models, etc.</summary>
    public LookupsClient Lookups { get; }

    /// <summary>Bulk ECU file import (Business tier only).</summary>
    public BulkImportClient BulkImport { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new Chiptuning.Ai client.
    /// </summary>
    /// <param name="baseUrl">
    /// Base URL of the API. Defaults to <c>https://www.chiptuning.ai</c>.
    /// Override for self-hosted or staging environments.
    /// </param>
    public ChiptuningAiClient(string baseUrl = "https://www.chiptuning.ai")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + '/') };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        Auth       = new AuthClient(this);
        Files      = new FilesClient(this);
        Patches    = new PatchesClient(this);
        Lookups    = new LookupsClient(this);
        BulkImport = new BulkImportClient(this);
    }

    /// <summary>
    /// Creates a client pre-loaded with an existing access token.
    /// Use this if you store tokens between sessions.
    /// </summary>
    /// <param name="accessToken">A valid JWT access token.</param>
    /// <param name="refreshToken">The corresponding refresh token.</param>
    /// <param name="baseUrl">Base URL of the API.</param>
    public static ChiptuningAiClient FromToken(
        string accessToken, string refreshToken,
        string baseUrl = "https://www.chiptuning.ai")
    {
        var client = new ChiptuningAiClient(baseUrl);
        client.SetTokens(accessToken, refreshToken);
        return client;
    }

    /// <summary>
    /// Creates a client pre-loaded with an existing access token and a known expiry time.
    /// Use this when restoring a persisted session so the refresh timer schedules correctly.
    /// </summary>
    public static ChiptuningAiClient FromToken(
        string accessToken, string refreshToken,
        string baseUrl, DateTimeOffset? expiresAt)
    {
        var client = new ChiptuningAiClient(baseUrl);
        client.SetTokens(accessToken, refreshToken);
        client.TokenExpiresAt = expiresAt;
        return client;
    }

    // ── Internal HTTP helpers ─────────────────────────────────────────────────

    internal void SetTokens(string? accessToken, string? refreshToken, int expiresInSeconds = 0)
    {
        _accessToken  = accessToken;
        RefreshToken  = refreshToken;
        TokenExpiresAt = accessToken != null && expiresInSeconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
            : null;
    }

    internal async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct) where TResponse : class
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<TResponse>(request, authenticate: true, ct);
    }

    internal async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest? body, bool authenticate, CancellationToken ct) where TResponse : class
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await SendAsync<TResponse>(request, authenticate, ct);
    }

    internal async Task<TResponse> PostFormAsync<TResponse>(
        string path, MultipartFormDataContent form, CancellationToken ct) where TResponse : class
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        return await SendAsync<TResponse>(request, authenticate: true, ct);
    }

    internal async Task DeleteAsync(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        await SendAsync<object>(request, authenticate: true, ct);
    }

    internal async Task PatchAsync<TBody>(string path, TBody body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        await SendAsync<object>(request, authenticate: true, ct);
    }

    /// <summary>
    /// GETs a resource and returns the raw response bytes (for binary file downloads).
    /// Throws <see cref="ApiException"/> if the server returns a non-success status.
    /// </summary>
    internal async Task<byte[]> GetBinaryAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_accessToken))
            throw new InvalidOperationException("Not authenticated. Call client.Auth.LoginAsync() first.");

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var error = TryDeserialize<ApiResponse<object>>(json);
            var message = error?.Error?.Message ?? error?.Message
                ?? (string.IsNullOrWhiteSpace(json) ? response.ReasonPhrase : json)
                ?? "Unknown error";
            throw new ApiException(message, error?.Error?.Code, (int)response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// POSTs multipart form data and returns the raw response bytes (for binary file downloads).
    /// Throws <see cref="ApiException"/> if the server returns a non-success status.
    /// </summary>
    internal async Task<byte[]> PostFormBinaryAsync(
        string path, MultipartFormDataContent form, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_accessToken))
            throw new InvalidOperationException("Not authenticated. Call client.Auth.LoginAsync() first.");

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var error = TryDeserialize<ApiResponse<object>>(json);
            var message = error?.Error?.Message ?? error?.Message
                ?? (string.IsNullOrWhiteSpace(json) ? response.ReasonPhrase : json)
                ?? "Unknown error";
            throw new ApiException(message, error?.Error?.Code, (int)response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpRequestMessage request, bool authenticate, CancellationToken ct) where TResponse : class
    {
        if (authenticate)
        {
            if (string.IsNullOrEmpty(_accessToken))
                throw new InvalidOperationException("Not authenticated. Call client.Auth.LoginAsync() first.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        var response = await _http.SendAsync(request, ct);

        // Auto-refresh on 401 then retry once
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && authenticate && RefreshToken is not null)
        {
            await Auth.RefreshAsync(ct);
            request = CloneRequest(request);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            response = await _http.SendAsync(request, ct);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new DailyLimitExceededException();

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<ApiResponse<object>>(json);
            var message = error?.Error?.Message ?? error?.Message
                ?? (string.IsNullOrWhiteSpace(json) ? response.ReasonPhrase : json)
                ?? "Unknown error";
            throw new ApiException(message, error?.Error?.Code, (int)response.StatusCode);
        }

        if (typeof(TResponse) == typeof(object)) return default!;

        var envelope = TryDeserialize<ApiResponse<TResponse>>(json);
        return envelope?.Data ?? TryDeserialize<TResponse>(json)!;
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return default; }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
