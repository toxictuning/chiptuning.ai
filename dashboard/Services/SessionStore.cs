using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChiptuningAi.Dashboard.Services;

internal sealed class SavedSession
{
    public string ApiUrl       { get; init; } = string.Empty;
    public string Email        { get; init; } = string.Empty;
    public string AccessToken  { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}

internal static class SessionStore
{
    private static readonly string _path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChiptuningAi", "session.dat");

    public static void Save(string apiUrl, string email, string accessToken,
                            string refreshToken, DateTimeOffset? expiresAt)
    {
        try
        {
            var json = JsonSerializer.Serialize(new SavedSession
            {
                ApiUrl       = apiUrl,
                Email        = email,
                AccessToken  = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt    = expiresAt,
            });
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, encrypted);
        }
        catch { }
    }

    public static SavedSession? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<SavedSession>(json);
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { }
    }
}
