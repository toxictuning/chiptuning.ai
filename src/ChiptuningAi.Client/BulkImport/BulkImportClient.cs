using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ChiptuningAi.Client.BulkImport;

/// <summary>
/// Bulk ECU file import operations.
/// Access via <see cref="ChiptuningAiClient.BulkImport"/>.
/// Requires the Business subscription tier.
/// </summary>
public sealed class BulkImportClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ChiptuningAiClient _owner;

    internal BulkImportClient(ChiptuningAiClient owner) => _owner = owner;

    /// <summary>
    /// Sends a list of filenames to the server for parsing.
    /// Returns per-file metadata extracted from the filename convention (no files are stored).
    /// </summary>
    /// <exception cref="Common.ApiException">Thrown on non-success HTTP status, including 403 if not Business tier.</exception>
    public async Task<IReadOnlyList<ParsedFileDto>> ParseFilenamesAsync(
        IEnumerable<string> filenames, CancellationToken ct = default)
    {
        var result = await _owner.PostAsync<object, List<ParsedFileDto>>(
            "api/import/parse",
            new { filenames = filenames.ToArray() },
            authenticate: true, ct);

        return result ?? [];
    }

    /// <summary>
    /// Uploads a single ECU file together with its pre-parsed metadata.
    /// Call once per file after <see cref="ParseFilenamesAsync"/>.
    /// </summary>
    /// <param name="filePath">Full local path to the ECU binary file.</param>
    /// <param name="metadata">Parsed metadata returned by <see cref="ParseFilenamesAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="Common.ApiException">Thrown on API error (403 = tier, 429 = limit reached).</exception>
    public async Task<BulkImportFileResult> ImportFileAsync(
        string filePath, ParsedFileDto metadata, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ECU file not found.", filePath);

        using var form = new MultipartFormDataContent();

        var bytes       = await File.ReadAllBytesAsync(filePath, ct);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        var metaJson = JsonSerializer.Serialize(metadata, JsonOpts);
        form.Add(new StringContent(metaJson, Encoding.UTF8, "text/plain"), "metadata");

        return await _owner.PostFormAsync<BulkImportFileResult>("api/import/upload", form, ct);
    }
}
