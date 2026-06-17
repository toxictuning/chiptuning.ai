namespace ChiptuningAi.Client.Common;

/// <summary>
/// Thrown when the Chiptuning.Ai API returns a non-success response.
/// Check <see cref="ErrorCode"/> for the machine-readable reason.
/// </summary>
public class ApiException : Exception
{
    /// <summary>Machine-readable error code returned by the API (e.g. <c>FILE_NOT_FOUND</c>).</summary>
    public string? ErrorCode { get; }

    /// <summary>HTTP status code of the failed response.</summary>
    public int StatusCode { get; }

    /// <inheritdoc/>
    public ApiException(string message, string? errorCode, int statusCode)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

/// <summary>
/// Thrown when <c>POST /api/patches/apply</c> returns HTTP 429 because the
/// account's daily patch-apply quota has been exhausted.
/// </summary>
public sealed class DailyLimitExceededException : ApiException
{
    /// <inheritdoc/>
    public DailyLimitExceededException()
        : base("Daily patch-apply limit reached. Upgrade your plan or try again tomorrow.", "DAILY_LIMIT_REACHED", 429) { }
}
