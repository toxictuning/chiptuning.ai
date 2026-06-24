using ChiptuningAi.Client.Common;

namespace ChiptuningAi.Client.BugReports;

public sealed class BugReportsClient(ChiptuningAiClient root)
{
    public Task<BugReportDto> SubmitAsync(
        string title, string description, string? stepsToReproduce = null,
        CancellationToken ct = default)
        => root.PostAsync<SubmitBugReportRequest, BugReportDto>(
            "api/bug-reports",
            new SubmitBugReportRequest
            {
                Title            = title,
                Description      = description,
                StepsToReproduce = stepsToReproduce,
                Source           = "wpf",
            },
            authenticate: true, ct);
}
