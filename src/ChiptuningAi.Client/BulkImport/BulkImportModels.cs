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
