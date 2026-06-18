using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChiptuningAi.Client.Auth;

/// <summary>Returned after a successful login or token refresh.</summary>
public sealed class TokenResponse
{
    // Accept the two most common field names for the access token.
    /// <summary>Primary token field name used by some API versions.</summary>
    [JsonPropertyName("token")]
    public string? TokenField { get; init; }

    /// <summary>Alternate camelCase spelling some API versions use.</summary>
    [JsonPropertyName("accessToken")]
    public string? AccessTokenField { get; init; }

    /// <summary>Alternate snake_case spelling some API versions use.</summary>
    [JsonPropertyName("access_token")]
    public string? AccessTokenSnakeField { get; init; }

    /// <summary>JWT access token, resolved from whichever field the server sent.</summary>
    [JsonIgnore]
    public string Token => TokenField ?? AccessTokenField ?? AccessTokenSnakeField ?? string.Empty;

    /// <summary>Opaque refresh token used to obtain a new access token without re-entering credentials.</summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>Number of seconds until the access token expires.</summary>
    public int ExpiresIn { get; init; }

    /// <summary>Captures any extra fields so we can show them in diagnostics if the token is missing.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; init; }
}

/// <summary>Authenticated user profile returned by <c>GET /api/auth/me</c>.</summary>
public sealed class UserProfile
{
    /// <summary>Unique user identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Account email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Subscription tier (e.g. <c>Basic</c>, <c>Premium</c>).</summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>Total storage consumed in bytes.</summary>
    public long StorageUsedBytes { get; init; }

    /// <summary>Storage allowance for the current tier in bytes.</summary>
    public long StorageLimitBytes { get; init; }
}
