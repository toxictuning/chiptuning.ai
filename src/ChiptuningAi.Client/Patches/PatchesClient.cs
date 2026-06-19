using ChiptuningAi.Client.Common;

namespace ChiptuningAi.Client.Patches;

/// <summary>
/// Operations for creating and applying ECU patches.
/// Access via <see cref="ChiptuningAiClient.Patches"/>.
/// </summary>
public sealed class PatchesClient
{
    private readonly ChiptuningAiClient _client;

    internal PatchesClient(ChiptuningAiClient client) => _client = client;

    /// <summary>
    /// Uploads a modified ECU file to generate a binary patch against its original parent.
    /// </summary>
    /// <param name="modifiedFilePath">
    /// Path to the tuned/modified ECU file. The API computes the binary diff
    /// between this file and the original identified by <paramref name="parentFileId"/>.
    /// </param>
    /// <param name="parentFileId">
    /// File ID of the original (unmodified) ECU file. Must have been uploaded
    /// with <see cref="Files.FilesClient.UploadAsync"/> first.
    /// </param>
    /// <param name="description">Optional human-readable description of the changes (e.g. <c>Stage 1 — +30 hp</c>).</param>
    /// <param name="version">Optional version label (e.g. <c>v1.0</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created patch including its <see cref="PatchUploadResult.PatchId"/>.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the modified file does not exist.</exception>
    /// <exception cref="ApiException">Thrown if the API rejects the request.</exception>
    public async Task<PatchUploadResult> UploadAsync(
        string modifiedFilePath,
        Guid parentFileId,
        string? description = null,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modifiedFilePath))
            throw new FileNotFoundException("Modified ECU file not found.", modifiedFilePath);

        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(File.OpenRead(modifiedFilePath));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "modifiedFile", Path.GetFileName(modifiedFilePath));
        form.Add(new StringContent(parentFileId.ToString()), "parentFileId");
        if (description is not null) form.Add(new StringContent(description), "description");
        if (version is not null)     form.Add(new StringContent(version),     "version");

        return await _client.PostFormAsync<PatchUploadResult>("/api/patches/upload", form, cancellationToken);
    }

    /// <summary>
    /// Applies a patch to a source ECU file and returns the patched output file.
    /// Each application counts against your daily plan quota.
    /// </summary>
    /// <param name="patchId">The patch to apply.</param>
    /// <param name="sourceFileId">The source ECU file to apply the patch to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result containing the output <see cref="ApplyPatchResult.ResultFileId"/>.</returns>
    /// <exception cref="DailyLimitExceededException">
    /// Thrown when the account's daily patch-apply quota has been exhausted.
    /// </exception>
    /// <exception cref="ApiException">Thrown if the API rejects the request.</exception>
    public Task<ApplyPatchResult> ApplyAsync(
        Guid patchId, Guid sourceFileId,
        CancellationToken cancellationToken = default)
    {
        var body = new { patchId, sourceFileId };
        return _client.PostAsync<object, ApplyPatchResult>(
            "/api/patches/apply", body, authenticate: true, cancellationToken);
    }

    /// <summary>
    /// Lists all patches created against a specific parent file.
    /// </summary>
    /// <param name="parentFileId">The parent file ID to list patches for.</param>
    /// <param name="skip">Pagination offset.</param>
    /// <param name="take">Page size, maximum 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PagedResult<PatchDetails>> ListAsync(
        Guid parentFileId, int skip = 0, int take = 20,
        CancellationToken cancellationToken = default)
        => _client.GetAsync<PagedResult<PatchDetails>>(
            $"/api/patches/parent/{parentFileId}?skip={skip}&take={take}", cancellationToken);

    /// <summary>
    /// Returns details for a specific patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PatchDetails> GetAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _client.GetAsync<PatchDetails>($"/api/patches/{patchId}", cancellationToken);

    /// <summary>
    /// Returns the patch application history for a file — every time a patch was applied to it.
    /// </summary>
    /// <param name="fileId">The file to get history for.</param>
    /// <param name="skip">Pagination offset.</param>
    /// <param name="take">Page size, maximum 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PagedResult<PatchHistoryEntry>> GetHistoryAsync(
        Guid fileId, int skip = 0, int take = 20,
        CancellationToken cancellationToken = default)
        => _client.GetAsync<PagedResult<PatchHistoryEntry>>(
            $"/api/patches/{fileId}/history?skip={skip}&take={take}", cancellationToken);

    /// <summary>Updates the description and/or version label of an existing patch.</summary>
    /// <param name="patchId">The patch identifier to update.</param>
    /// <param name="description">New description, or null to clear.</param>
    /// <param name="version">New version label, or null to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task UpdateAsync(
        Guid patchId,
        string? description,
        string? version,
        CancellationToken cancellationToken = default)
    {
        var body = new { description, version };
        return _client.PatchAsync($"/api/patches/{patchId}", body, cancellationToken);
    }

    /// <summary>Permanently deletes a patch.</summary>
    /// <param name="patchId">The patch identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeleteAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _client.DeleteAsync($"/api/patches/{patchId}", cancellationToken);

    /// <summary>
    /// Applies multiple patches to a raw ECU file and returns the processed binary.
    /// Patches may come from different parent files; they are applied in the order given.
    /// The file is not stored — the result is streamed back directly.
    /// </summary>
    /// <param name="filePath">Path to the raw ECU binary to process.</param>
    /// <param name="patchIds">Ordered list of patch IDs to apply (sort by similarity before calling).</param>
    /// <param name="bypassIntegrity">Skip the integrity check against the base file.</param>
    /// <param name="bypassReason">Optional reason logged server-side when bypassing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processed binary bytes.</returns>
    public async Task<byte[]> ProcessAsync(
        string filePath,
        IReadOnlyList<Guid> patchIds,
        bool bypassIntegrity = false,
        string? bypassReason = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ECU file not found.", filePath);

        using var form = new MultipartFormDataContent();

        var fileContent = new StreamContent(File.OpenRead(filePath));
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        var idsJson = System.Text.Json.JsonSerializer.Serialize(patchIds);
        form.Add(new StringContent(idsJson), "patchIds");
        form.Add(new StringContent(bypassIntegrity ? "true" : "false"), "bypassIntegrity");
        if (bypassReason is not null)
            form.Add(new StringContent(bypassReason), "bypassReason");

        return await _client.PostFormBinaryAsync("/api/patches/process", form, cancellationToken);
    }

    /// <summary>
    /// Detects which patches for a parent file are already applied, not applied, or conflicted
    /// in the supplied ECU binary. Returns one result per active patch with metadata and state.
    /// </summary>
    /// <param name="parentFileId">Parent file whose patches to check against.</param>
    /// <param name="filePath">Path to the ECU binary to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<List<PatchDetectionResult>> DetectAsync(
        Guid parentFileId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ECU file not found.", filePath);

        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(File.OpenRead(filePath));
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        return await _client.PostFormAsync<List<PatchDetectionResult>>(
            $"/api/patches/detect/{parentFileId}", form, cancellationToken);
    }

    /// <summary>
    /// Applies, removes, or skips patches on a source ECU file per explicit per-patch actions.
    /// Apply writes the solution bytes; Remove restores the original master bytes back.
    /// The result binary is returned directly — not stored server-side.
    /// </summary>
    /// <param name="filePath">Source ECU binary to process.</param>
    /// <param name="parentFileId">Parent file the patches belong to (used for audit).</param>
    /// <param name="actions">Per-patch actions. Patches with action Skip are no-ops.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processed binary bytes.</returns>
    public async Task<byte[]> GenerateAsync(
        string filePath,
        Guid parentFileId,
        IReadOnlyList<PatchClientAction> actions,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ECU file not found.", filePath);

        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(File.OpenRead(filePath));
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(parentFileId.ToString()), "parentFileId");
        var actionsJson = System.Text.Json.JsonSerializer.Serialize(actions);
        form.Add(new StringContent(actionsJson), "actions");

        return await _client.PostFormBinaryAsync("/api/patches/generate", form, cancellationToken);
    }
}
