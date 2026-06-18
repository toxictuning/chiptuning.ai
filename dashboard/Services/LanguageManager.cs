using System.IO;
using System.Windows;

namespace ChiptuningAi.Dashboard.Services;

public static class LanguageManager
{
    private static readonly string _settingsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChiptuningAi", "language.txt");

    public static string CurrentCode { get; private set; } = "en";

    public static readonly IReadOnlyList<(string Code, string Flag, string Display)> Languages =
    [
        ("en", "🇬🇧", "English"),
        ("fr", "🇫🇷", "Français"),
        ("it", "🇮🇹", "Italiano"),
        ("es", "🇪🇸", "Español"),
        ("de", "🇩🇪", "Deutsch"),
        ("hi", "🇮🇳", "हिंदी"),
        ("ko", "🇰🇷", "한국어"),
        ("zh", "🇨🇳", "中文"),
        ("nl", "🇳🇱", "Nederlands"),
        ("th", "🇹🇭", "ภาษาไทย"),
    ];

    public static void Apply(string? code = null)
    {
        CurrentCode = code ?? Load();

        var uri  = new Uri($"Languages/{CurrentCode}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        // Index 0 = theme, index 1 = language
        if (merged.Count > 1)
            merged[1] = dict;
        else
            merged.Add(dict);

        if (code is not null) Save(code);
    }

    public static string Get(string key)
        => Application.Current.Resources[key] as string ?? key;

    private static string Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var saved = File.ReadAllText(_settingsPath).Trim();
                if (Languages.Any(l => l.Code == saved)) return saved;
            }
        }
        catch { }
        return "en";
    }

    private static void Save(string code)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, code);
        }
        catch { }
    }
}
