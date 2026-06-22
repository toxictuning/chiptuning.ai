namespace ChiptuningAi.Client.Patches;

/// <summary>Result returned after a patch is created.</summary>
public sealed class PatchUploadResult
{
    /// <summary>Unique patch identifier. Store this to apply the patch to other files.</summary>
    public Guid PatchId { get; init; }

    /// <summary>Patch file size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Human-readable description of the patch changes.</summary>
    public string? Description { get; init; }

    /// <summary>Version label (e.g. <c>v1.2</c>).</summary>
    public string? Version { get; init; }
}

/// <summary>Detailed patch information.</summary>
public sealed class PatchDetails
{
    /// <summary>Unique patch identifier.</summary>
    public Guid PatchId { get; init; }

    /// <summary>Description of the changes in this patch.</summary>
    public string? Description { get; init; }

    /// <summary>Version label.</summary>
    public string? Version { get; init; }

    /// <summary>Patch file size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Whether the patch is currently active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Date and time the patch was created (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Result returned after successfully applying a patch.</summary>
public sealed class ApplyPatchResult
{
    /// <summary>File ID of the patched output file.</summary>
    public Guid ResultFileId { get; init; }

    /// <summary>Date and time the patch was applied (UTC).</summary>
    public DateTime AppliedAt { get; init; }
}

/// <summary>A single entry in a file's patch application history.</summary>
public sealed class PatchHistoryEntry
{
    /// <summary>The patch that was applied.</summary>
    public Guid PatchId { get; init; }

    /// <summary>The output file produced.</summary>
    public Guid ResultFileId { get; init; }

    /// <summary>When the patch was applied (UTC).</summary>
    public DateTime AppliedAt { get; init; }
}

/// <summary>
/// Detection result for one patch against a user-supplied ECU file.
/// Returned by <see cref="PatchesClient.DetectAsync"/>.
/// </summary>
public sealed class PatchDetectionResult
{
    /// <summary>Unique identifier of the patch.</summary>
    public Guid     PatchId     { get; init; }
    /// <summary>Human-readable description of the patch.</summary>
    public string?  Description { get; init; }
    /// <summary>Version label of the patch.</summary>
    public string?  Version     { get; init; }
    /// <summary>Size of the patch file in kilobytes.</summary>
    public double   SizeKb      { get; init; }
    /// <summary>UTC timestamp when the patch was created.</summary>
    public DateTime CreatedAt   { get; init; }
    /// <summary>"Applied" or "NotApplied".</summary>
    public string State { get; init; } = "NotApplied";
}

/// <summary>
/// Per-patch action for <see cref="PatchesClient.GenerateAsync"/>.
/// </summary>
public sealed class PatchClientAction
{
    /// <summary>Unique identifier of the patch to act on.</summary>
    public Guid   PatchId { get; init; }
    /// <summary>"Apply", "Remove", or "Skip".</summary>
    public string Action  { get; init; } = "Skip";
}

/// <summary>
/// Per-patch action for <see cref="PatchesClient.GenerateMultiAsync"/>.
/// Carries a ParentFileId so patches from multiple similar files can be applied in one call.
/// </summary>
public sealed class MultiPatchClientAction
{
    public Guid   ParentFileId { get; init; }
    public Guid   PatchId      { get; init; }
    public string Action       { get; init; } = "Skip";
}
