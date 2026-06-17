namespace ChiptuningAi.Client.Common;

/// <summary>Represents the standard JSON envelope returned by every Chiptuning.Ai API endpoint.</summary>
internal sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public ApiErrorDetail? Error { get; init; }
}

internal sealed class ApiErrorDetail
{
    public string? Code { get; init; }
    public string? Message { get; init; }
}

/// <summary>Paged result wrapper returned by list endpoints.</summary>
public sealed class PagedResult<T>
{
    /// <summary>Items in the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Total number of records matching the query.</summary>
    public int Total { get; init; }
}
