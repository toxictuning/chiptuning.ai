using System.Windows;
using System.Windows.Media;

namespace ChiptuningAi.Dashboard.Services;

internal static class ThemeManager
{
    private static AppSettings _settings = AppSettings.Load();

    public static bool IsDark       => _settings.IsDark;
    public static string CurrentSwatch => _settings.Swatch;

    // key → (display colour, dark-mode accent, dark-mode fg, light-mode accent, light-mode fg)
    public static readonly (string Key, Color Display, Color DarkAccent, Color DarkFg, Color LightAccent, Color LightFg)[] Swatches =
    [
        ("Mono",   Color.FromRgb(136, 136, 136), Color.FromRgb(232, 232, 232), Color.FromRgb(12, 12, 12),  Color.FromRgb(12, 12, 12),  Color.FromRgb(240, 240, 240)),
        ("Blue",   Color.FromRgb(59,  130, 246), Color.FromRgb(59,  130, 246), Colors.White,               Color.FromRgb(59,  130, 246), Colors.White),
        ("Green",  Color.FromRgb(34,  197, 94),  Color.FromRgb(34,  197, 94),  Colors.White,               Color.FromRgb(34,  197, 94),  Colors.White),
        ("Red",    Color.FromRgb(239, 68,  68),  Color.FromRgb(239, 68,  68),  Colors.White,               Color.FromRgb(239, 68,  68),  Colors.White),
        ("Purple", Color.FromRgb(139, 92,  246), Color.FromRgb(139, 92,  246), Colors.White,               Color.FromRgb(139, 92,  246), Colors.White),
        ("Amber",  Color.FromRgb(245, 158, 11),  Color.FromRgb(245, 158, 11),  Color.FromRgb(12, 12, 12),  Color.FromRgb(245, 158, 11),  Color.FromRgb(12, 12, 12)),
    ];

    public static void Apply()
    {
        var uri = _settings.IsDark
            ? new Uri("Themes/Dark.xaml",  UriKind.Relative)
            : new Uri("Themes/Light.xaml", UriKind.Relative);

        var merged = Application.Current.Resources.MergedDictionaries;
        // Index 0 is the theme dict; index 1 is Styles (added in App.xaml statically)
        if (merged.Count == 0)
            merged.Add(new ResourceDictionary { Source = uri });
        else
            merged[0] = new ResourceDictionary { Source = uri };

        ApplySwatch(_settings.Swatch);
    }

    public static void SetTheme(bool isDark)
    {
        _settings.IsDark = isDark;
        _settings.Save();
        Apply();
    }

    public static void SetSwatch(string key)
    {
        _settings.Swatch = key;
        _settings.Save();
        ApplySwatch(key);
    }

    private static void ApplySwatch(string key)
    {
        var sw = Array.Find(Swatches, s => s.Key == key);
        if (sw == default) sw = Swatches[0];

        var accent   = _settings.IsDark ? sw.DarkAccent : sw.LightAccent;
        var accentFg = _settings.IsDark ? sw.DarkFg     : sw.LightFg;

        var res = Application.Current.Resources;
        res["AccentButtonBg"]  = new SolidColorBrush(accent);
        res["AccentButtonFg"]  = new SolidColorBrush(accentFg);
        res["InputFocusBrush"] = new SolidColorBrush(accent);
        res["InputFocusColor"] = accent;   // Color resource used in inline SolidColorBrush
    }
}
