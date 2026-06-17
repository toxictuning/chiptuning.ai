using ChiptuningAi.Client.Common;

namespace ChiptuningAi.Client.Auth;

/// <summary>
/// Handles authentication against the Chiptuning.Ai API.
/// Tokens are managed automatically by <see cref="ChiptuningAiClient"/> —
/// you only need to call <see cref="LoginAsync"/> once per session.
/// </summary>
public sealed class AuthClient
{
    private readonly ChiptuningAiClient _client;

    internal AuthClient(ChiptuningAiClient client) => _client = client;

    /// <summary>
    /// Authenticates with the API and stores the resulting tokens for all subsequent requests.
    /// </summary>
    /// <param name="email">Account email address.</param>
    /// <param name="password">Account password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token response including access token, refresh token, and expiry.</returns>
    /// <exception cref="ApiException">Thrown if credentials are invalid.</exception>
    public async Task<TokenResponse> LoginAsync(
        string email, string password,
        CancellationToken cancellationToken = default)
    {
        var body = new { email, password };
        var response = await _client.PostAsync<object, TokenResponse>(
            "/api/auth/login", body, authenticate: false, cancellationToken);

        if (string.IsNullOrEmpty(response.Token))
        {
            var known = $"token={response.TokenField ?? "–"}, accessToken={response.AccessTokenField ?? "–"}, access_token={response.AccessTokenSnakeField ?? "–"}";
            var extra = response.ExtraFields is { Count: > 0 }
                ? string.Join(", ", response.ExtraFields.Keys)
                : "(none)";
            throw new ApiException(
                $"Server did not return an access token. Received fields — {known}; other: {extra}", null, 0);
        }

        _client.SetTokens(response.Token, response.RefreshToken, response.ExpiresIn);
        return response;
    }

    /// <summary>
    /// Exchanges the current refresh token for a new access token.
    /// Called automatically when a request returns HTTP 401.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TokenResponse> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = _client.RefreshToken
            ?? throw new InvalidOperationException("No refresh token available. Call LoginAsync first.");
        var body = new { refreshToken };
        var response = await _client.PostAsync<object, TokenResponse>(
            "/api/auth/refresh", body, authenticate: false, cancellationToken);
        _client.SetTokens(response.Token, response.RefreshToken, response.ExpiresIn);
        return response;
    }

    /// <summary>Revokes the current session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _client.PostAsync<object, object>("/api/auth/logout", null, authenticate: true, cancellationToken);
        _client.SetTokens(null, null);
    }

    /// <summary>Returns the authenticated user's profile and plan details.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<UserProfile> GetProfileAsync(CancellationToken cancellationToken = default)
        => _client.GetAsync<UserProfile>("/api/auth/me", cancellationToken);
}
