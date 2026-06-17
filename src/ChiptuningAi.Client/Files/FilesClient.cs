using ChiptuningAi.Client.Common;

namespace ChiptuningAi.Client.Files;

/// <summary>
/// Operations for uploading and managing ECU files.
/// Access via <see cref="ChiptuningAiClient.Files"/>.
/// </summary>
public sealed class FilesClient
{
    private const long SingleUploadLimit = 20L * 1024 * 1024; // 20 MB

    private readonly ChiptuningAiClient _client;

    internal FilesClient(ChiptuningAiClient client) => _client = client;

    /// <summary>
    /// Uploads an ECU file. Automatically uses chunked upload for files larger than 20 MB.
    /// </summary>
    /// <param name="filePath">Full path to the ECU file on disk.</param>
    /// <param name="metadata">Vehicle and controller metadata to associate with the file.</param>
    /// <param name="progress">
    /// Optional progress callback. Receives a value from 0 to 100 during chunked uploads.
    /// Not reported for single uploads.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The uploaded file result including the <see cref="FileUploadResult.FileId"/> needed to create patches.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist at <paramref name="filePath"/>.</exception>
    /// <exception cref="ApiException">Thrown if the API rejects the upload.</exception>
    public async Task<FileUploadResult> UploadAsync(
        string filePath,
        FileMetadata metadata,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ECU file not found.", filePath);

        var fileInfo = new FileInfo(filePath);

        return fileInfo.Length <= SingleUploadLimit
            ? await UploadSingleAsync(filePath, metadata, cancellationToken)
            : await UploadChunkedAsync(filePath, metadata, progress, cancellationToken);
    }

    /// <summary>
    /// Retrieves details for a specific file by ID.
    /// </summary>
    public Task<FileDetails> GetAsync(Guid fileId, CancellationToken cancellationToken = default)
        => _client.GetAsync<FileDetails>($"/api/files/{fileId}", cancellationToken);

    /// <summary>
    /// Looks up a file by its MD5 hash (exact match). Returns null if not found.
    /// </summary>
    public async Task<FileDetails?> GetByHashAsync(string md5, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _client.GetAsync<FileDetails>(
                $"/api/files/byhash/{Uri.EscapeDataString(md5)}", cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists files filtered by controller make and model.
    /// </summary>
    /// <param name="ecuMake">Controller manufacturer to filter by (e.g. <c>Bosch</c>).</param>
    /// <param name="ecuModel">Controller model to filter by (e.g. <c>EDC17C64</c>).</param>
    /// <param name="skip">Number of records to skip (for pagination).</param>
    /// <param name="take">Number of records to return. Maximum 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PagedResult<FileDetails>> ListAsync(
        string? ecuMake = null, string? ecuModel = null,
        int skip = 0, int take = 20,
        CancellationToken cancellationToken = default)
    {
        var qs = new System.Text.StringBuilder($"?skip={skip}&take={take}");
        if (!string.IsNullOrWhiteSpace(ecuMake))
            qs.Append($"&ecuMake={Uri.EscapeDataString(ecuMake)}");
        if (!string.IsNullOrWhiteSpace(ecuModel))
            qs.Append($"&ecuModel={Uri.EscapeDataString(ecuModel)}");
        return _client.GetAsync<PagedResult<FileDetails>>($"/api/files{qs}", cancellationToken);
    }

    /// <summary>
    /// Finds files similar to the provided ECU file using Jaccard similarity on 4 KB MD5 chunks.
    /// Useful for finding tuned versions of the same base file.
    /// </summary>
    /// <param name="filePath">Path to the file to compare against.</param>
    /// <param name="ecuMake">Limit search to this controller manufacturer. Omitted when null or empty.</param>
    /// <param name="ecuModel">Limit search to this controller model. Omitted when null or empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of similar files ordered by descending similarity score.</returns>
    public async Task<IReadOnlyList<SimilarFile>> FindSimilarAsync(
        string filePath,
        string? ecuMake = null,
        string? ecuModel = null,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(CreateFileContent(filePath), "file", Path.GetFileName(filePath));
        if (!string.IsNullOrWhiteSpace(ecuMake))
            form.Add(new StringContent(ecuMake), "ecuMake");
        if (!string.IsNullOrWhiteSpace(ecuModel))
            form.Add(new StringContent(ecuModel), "ecuModel");

        return await _client.PostFormAsync<IReadOnlyList<SimilarFile>>(
            "/api/files/similar", form, cancellationToken);
    }

    /// <summary>
    /// Soft-deletes a file. The file is hidden immediately and permanently purged after 30 days.
    /// </summary>
    /// <param name="fileId">The file identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
        => _client.DeleteAsync($"/api/files/{fileId}", cancellationToken);

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<FileUploadResult> UploadSingleAsync(
        string filePath, FileMetadata metadata, CancellationToken ct)
    {
        using var form = BuildMetadataForm(metadata);
        form.Add(CreateFileContent(filePath), "file", Path.GetFileName(filePath));
        return await _client.PostFormAsync<FileUploadResult>("/api/files/upload", form, ct);
    }

    private async Task<FileUploadResult> UploadChunkedAsync(
        string filePath, FileMetadata metadata, IProgress<int>? progress, CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);

        // 1. Start session
        var startBody = new
        {
            fileName         = fileInfo.Name,
            fileSize         = fileInfo.Length,
            vehicleClass     = metadata.VehicleClass,
            vehicleMake      = metadata.VehicleMake,
            vehicleModel     = metadata.VehicleModel,
            vehicleVariant   = metadata.VehicleVariant,
            engineType       = metadata.EngineType,
            ecuType          = metadata.ECUType,
            ecuMake          = metadata.ECUMake,
            ecuModel         = metadata.ECUModel,
            readHardware     = metadata.ReadHardware,
            readMode         = metadata.ReadMode,
            controllerHWNumber = metadata.ControllerHWNumber,
            controllerSWNumber = metadata.ControllerSWNumber,
            engineCode       = metadata.EngineCode,
            vin              = metadata.VIN,
            powerOutput      = metadata.PowerOutput,
            torqueOutput     = metadata.TorqueOutput,
        };

        var session = await _client.PostAsync<object, ChunkedSessionResponse>(
            "/api/files/upload/start", startBody, authenticate: true, ct);

        // 2. Upload chunks
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[session.ChunkSize];

        for (int i = 0; i < session.TotalChunks; i++)
        {
            ct.ThrowIfCancellationRequested();

            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, session.ChunkSize), ct);
            var chunkData = buffer[..bytesRead];

            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(chunkData), "chunk", $"chunk_{i}");

            await _client.PostFormAsync<object>(
                $"/api/files/upload/{session.SessionId}/chunk/{i}", form, ct);

            progress?.Report((int)((i + 1) * 100.0 / session.TotalChunks));
        }

        // 3. Complete
        return await _client.PostAsync<object, FileUploadResult>(
            $"/api/files/upload/{session.SessionId}/complete", null, authenticate: true, ct);
    }

    private static MultipartFormDataContent BuildMetadataForm(FileMetadata m)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(m.VehicleClass),   "vehicleClass");
        form.Add(new StringContent(m.VehicleMake),    "vehicleMake");
        form.Add(new StringContent(m.VehicleModel),   "vehicleModel");
        form.Add(new StringContent(m.VehicleVariant), "vehicleVariant");
        form.Add(new StringContent(m.EngineType),     "engineType");
        form.Add(new StringContent(m.ECUType),        "ecuType");
        form.Add(new StringContent(m.ECUMake),        "ecuMake");
        form.Add(new StringContent(m.ECUModel),       "ecuModel");
        form.Add(new StringContent(m.ReadHardware),   "readHardware");
        form.Add(new StringContent(m.ReadMode),       "readMode");
        if (m.ControllerHWNumber is not null) form.Add(new StringContent(m.ControllerHWNumber), "controllerHWNumber");
        if (m.ControllerSWNumber is not null) form.Add(new StringContent(m.ControllerSWNumber), "controllerSWNumber");
        if (m.EngineCode is not null)         form.Add(new StringContent(m.EngineCode),         "engineCode");
        if (m.VIN is not null)                form.Add(new StringContent(m.VIN),                "vin");
        if (m.PowerOutput.HasValue)           form.Add(new StringContent(m.PowerOutput.Value.ToString()), "powerOutput");
        if (m.TorqueOutput.HasValue)          form.Add(new StringContent(m.TorqueOutput.Value.ToString()), "torqueOutput");
        return form;
    }

    private static StreamContent CreateFileContent(string filePath)
    {
        var content = new StreamContent(File.OpenRead(filePath));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private sealed class ChunkedSessionResponse
    {
        public Guid SessionId { get; init; }
        public int ChunkSize { get; init; }
        public int TotalChunks { get; init; }
    }
}
