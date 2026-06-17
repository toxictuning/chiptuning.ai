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

    /// <summary>Permanently deletes a patch.</summary>
    /// <param name="patchId">The patch identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeleteAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _client.DeleteAsync($"/api/patches/{patchId}", cancellationToken);
}
