namespace ChiptuningAi.Client.BugReports;

public sealed class SubmitBugReportRequest
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? StepsToReproduce { get; init; }
    public string Source { get; init; } = "wpf";
}

public sealed class BugReportDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
