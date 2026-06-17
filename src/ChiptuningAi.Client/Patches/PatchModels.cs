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
