using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ChiptuningAi.Dashboard.Services;

internal static class UpdateChecker
{
    private const string ApiUrl      = "https://api.github.com/repos/toxictuning/chiptuning.ai/releases/latest";
    public  const string ReleasesUrl = "https://github.com/toxictuning/chiptuning.ai/releases/latest";

    public static async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ChiptuningAi-Dashboard");
            var release = await http.GetFromJsonAsync<GhRelease>(ApiUrl);
            return release?.TagName?.TrimStart('v');
        }
        catch
        {
            return null;
        }
    }

    public static bool IsNewer(string? remoteVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteVersion)) return false;
        if (!Version.TryParse(remoteVersion, out var remote)) return false;

        var localFull = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        // Normalize both to Major.Minor.Build — CLR pads assembly version with a 4th zero
        // which would make the local version appear newer than a 3-part GitHub tag.
        var local = new Version(localFull.Major, Math.Max(0, localFull.Minor), Math.Max(0, localFull.Build));
        var norm  = new Version(remote.Major,    Math.Max(0, remote.Minor),    Math.Max(0, remote.Build));

        return norm > local;
    }

    private sealed record GhRelease(
        [property: JsonPropertyName("tag_name")] string? TagName);
}
