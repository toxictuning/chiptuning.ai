namespace ChiptuningAi.Client.BulkImport;

/// <summary>Metadata parsed from a single filename by the bulk-import parse endpoint.</summary>
public sealed class ParsedFileDto
{
    /// <summary>Original filename as submitted.</summary>
    public string OriginalName { get; init; } = string.Empty;

    /// <summary><c>Valid</c> if all required fields resolved successfully; <c>Invalid</c> otherwise.</summary>
    public string Status { get; init; } = string.Empty;

    public string? VehicleClass { get; init; }
    public string? VehicleMake  { get; init; }
    public string? VehicleModel { get; init; }
    public string? EngineType   { get; init; }
    public string? ECUMake      { get; init; }
    public string? ECUModel     { get; init; }
    public string? ECUType      { get; init; }
    public string? EngineCode   { get; init; }
    public int?    PowerOutput  { get; init; }

    /// <summary>%File.ReadHardware% value — e.g. "Autotuner OBD", "bFlash".</summary>
    public string? ReadHardware { get; init; }

    /// <summary>%More.Versionname% value — "Original", "OPF OFF", "Stage 1", etc.</summary>
    public string? VersionName { get; init; }

    /// <summary>%File.Filetitle% rejoined — WinOLS project ID shared by all variants of the same file. Used as group key to link patches to their Original.</summary>
    public string? GroupKey { get; init; }

    /// <summary>True when VersionName is "Original" (case-insensitive).</summary>
    public bool IsOriginal => string.Equals(VersionName, "Original", StringComparison.OrdinalIgnoreCase);

    /// <summary>Validation errors, if any.</summary>
    public string[] Errors { get; init; } = [];
}

/// <summary>Result of importing a single file through the bulk-import upload endpoint.</summary>
public sealed class BulkImportFileResult
{
    public string  FileName { get; init; } = string.Empty;
    public bool    Success  { get; init; }
    public Guid?   FileId   { get; init; }
    public string? Error    { get; init; }
}
