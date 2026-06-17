using System.Windows;
using ChiptuningAi.Client;
using ChiptuningAi.Dashboard.Services;

namespace ChiptuningAi.Dashboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Apply();

        var session = SessionStore.Load();
        if (session is { AccessToken: { Length: > 0 } } s)
        {
            try
            {
                var client = ChiptuningAiClient.FromToken(s.AccessToken, s.RefreshToken, s.ApiUrl);
                new MainWindow(client).Show();
                return;
            }
            catch { }
        }

        new LoginWindow().Show();
    }
}

