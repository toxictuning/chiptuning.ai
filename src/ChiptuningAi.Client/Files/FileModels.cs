namespace ChiptuningAi.Client.Files;

/// <summary>
/// Metadata describing the vehicle and controller for an ECU file upload.
/// Required fields must be provided; optional fields can be left null.
/// </summary>
public sealed class FileMetadata
{
    // ── Vehicle ──────────────────────────────────────────────────────────────

    /// <summary>Vehicle class (e.g. <c>Passenger Car</c>, <c>Truck</c>). Required.</summary>
    public required string VehicleClass { get; init; }

    /// <summary>Vehicle manufacturer (e.g. <c>BMW</c>). Required.</summary>
    public required string VehicleMake { get; init; }

    /// <summary>Vehicle model (e.g. <c>3 Series</c>). Required.</summary>
    public required string VehicleModel { get; init; }

    /// <summary>Vehicle variant (e.g. <c>320i</c>). Required.</summary>
    public required string VehicleVariant { get; init; }

    /// <summary>Fuel type: <c>Petrol</c> or <c>Diesel</c>. Required.</summary>
    public required string EngineType { get; init; }

    // ── Controller ───────────────────────────────────────────────────────────

    /// <summary>Controller type: <c>ECU</c>, <c>TCU</c>, or <c>CPC</c>. Required.</summary>
    public required string ECUType { get; init; }

    /// <summary>Controller manufacturer (e.g. <c>Bosch</c>). Required.</summary>
    public required string ECUMake { get; init; }

    /// <summary>Controller model (e.g. <c>EDC17C64</c>). Required.</summary>
    public required string ECUModel { get; init; }

    // ── Read info ────────────────────────────────────────────────────────────

    /// <summary>Tool used to read the controller (e.g. <c>CMD Flash</c>, <c>Alientech KESS3</c>). Required.</summary>
    public required string ReadHardware { get; init; }

    /// <summary>Read method: <c>OBD</c>, <c>Bench</c>, or <c>Boot</c>. Required.</summary>
    public required string ReadMode { get; init; }

    // ── Optional ─────────────────────────────────────────────────────────────

    /// <summary>Controller hardware number (optional).</summary>
    public string? ControllerHWNumber { get; init; }

    /// <summary>Controller software number (optional).</summary>
    public string? ControllerSWNumber { get; init; }

    /// <summary>Engine code (e.g. <c>N57D30</c>). Optional.</summary>
    public string? EngineCode { get; init; }

    /// <summary>Vehicle identification number. Optional.</summary>
    public string? VIN { get; init; }

    /// <summary>Stock power output in kW. Optional.</summary>
    public int? PowerOutput { get; init; }

    /// <summary>Stock torque output in Nm. Optional.</summary>
    public int? TorqueOutput { get; init; }
}

/// <summary>Result returned after a successful file upload.</summary>
public sealed class FileUploadResult
{
    /// <summary>Unique identifier for the uploaded file. Store this to create patches later.</summary>
    public Guid FileId { get; init; }

    /// <summary>Original file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>SHA-256 hash of the file content.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>MD5 hash of the file content.</summary>
    public string MD5 { get; init; } = string.Empty;

    /// <summary>
    /// True if this file was already uploaded previously.
    /// The existing record is returned instead of creating a duplicate.
    /// </summary>
    public bool IsDuplicate { get; init; }

    /// <summary>Date and time the file was uploaded (UTC).</summary>
    public DateTime UploadedAt { get; init; }
}

/// <summary>Detailed file information returned by get and list endpoints.</summary>
public sealed class FileDetails
{
    /// <summary>Unique file identifier.</summary>
    public Guid FileId { get; init; }

    /// <summary>Original file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>SHA-256 hash.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>MD5 hash.</summary>
    public string MD5 { get; init; } = string.Empty;

    /// <summary>Vehicle class.</summary>
    public string VehicleClass { get; init; } = string.Empty;

    /// <summary>Vehicle manufacturer.</summary>
    public string VehicleMake { get; init; } = string.Empty;

    /// <summary>Vehicle model.</summary>
    public string VehicleModel { get; init; } = string.Empty;

    /// <summary>Vehicle variant.</summary>
    public string VehicleVariant { get; init; } = string.Empty;

    /// <summary>Fuel type.</summary>
    public string EngineType { get; init; } = string.Empty;

    /// <summary>Controller type (ECU/TCU/CPC).</summary>
    public string ECUType { get; init; } = string.Empty;

    /// <summary>Controller manufacturer.</summary>
    public string ECUMake { get; init; } = string.Empty;

    /// <summary>Controller model.</summary>
    public string ECUModel { get; init; } = string.Empty;

    /// <summary>Read hardware tool.</summary>
    public string ReadHardware { get; init; } = string.Empty;

    /// <summary>Read mode (OBD/Bench/Boot).</summary>
    public string ReadMode { get; init; } = string.Empty;

    /// <summary>Controller hardware number.</summary>
    public string? ControllerHWNumber { get; init; }

    /// <summary>Controller software number.</summary>
    public string? ControllerSWNumber { get; init; }

    /// <summary>Engine code.</summary>
    public string? EngineCode { get; init; }

    /// <summary>Vehicle identification number.</summary>
    public string? VIN { get; init; }

    /// <summary>Stock power output in kW.</summary>
    public int? PowerOutput { get; init; }

    /// <summary>Stock torque output in Nm.</summary>
    public int? TorqueOutput { get; init; }

    /// <summary>Number of patches created against this file.</summary>
    public int PatchCount { get; init; }

    /// <summary>Date and time the file was uploaded (UTC).</summary>
    public DateTime UploadedAt { get; init; }

    /// <summary>User ID of the uploader.</summary>
    public Guid UploadedBy { get; init; }

    /// <summary>Email address of the uploader (populated on detail fetch only).</summary>
    public string? UploadedByEmail { get; init; }

    /// <summary>MD5 chunk hashes used for similarity matching (populated on detail fetch only).</summary>
    public IReadOnlyList<string>? ChunkHashes { get; init; }

    /// <summary>True when the authenticated caller owns this file.</summary>
    public bool IsOwner { get; init; }
}

/// <summary>Editable metadata fields for an existing file.</summary>
public sealed class UpdateFileRequest
{
    public required string VehicleClass { get; init; }
    public required string VehicleMake { get; init; }
    public required string VehicleModel { get; init; }
    public required string VehicleVariant { get; init; }
    public required string EngineType { get; init; }
    public required string ECUType { get; init; }
    public required string ECUMake { get; init; }
    public required string ECUModel { get; init; }
    public required string ReadHardware { get; init; }
    public required string ReadMode { get; init; }
    public string? ControllerHWNumber { get; init; }
    public string? ControllerSWNumber { get; init; }
}

/// <summary>A file match returned by the similarity search endpoint.</summary>
public sealed class SimilarFile
{
    /// <summary>File identifier.</summary>
    public Guid FileId { get; init; }

    /// <summary>File name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Jaccard similarity score between 0.0 and 1.0.</summary>
    public double JaccardScore { get; init; }

    /// <summary>Human-readable match percentage (e.g. "87.5%").</summary>
    public string MatchPercentage { get; init; } = string.Empty;
}
