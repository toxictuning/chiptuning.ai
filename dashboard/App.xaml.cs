using System.Windows;
using ChiptuningAi.Client;
using ChiptuningAi.Dashboard.Services;

namespace ChiptuningAi.Dashboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Info($"--- Dashboard started (v{GetType().Assembly.GetName().Version}) ---");

        ThemeManager.Apply();
        LanguageManager.Apply();

        var session = SessionStore.Load();
        if (session is { AccessToken: { Length: > 0 } } s)
        {
            try
            {
                var client = ChiptuningAiClient.FromToken(s.AccessToken, s.RefreshToken, s.ApiUrl);
                AppLogger.Info("Restored session from store");
                new MainWindow(client, s.ApiUrl, s.Email).Show();
                return;
            }
            catch (Exception ex) { AppLogger.Error("Failed to restore session", ex); }
        }

        new LoginWindow().Show();
    }
}

