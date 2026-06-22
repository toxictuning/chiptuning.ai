using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using ChiptuningAi.Client;
using ChiptuningAi.Client.Common;
using ChiptuningAi.Dashboard.Services;

namespace ChiptuningAi.Dashboard;

public partial class LoginWindow : Window
{
    private const string ApiUrl = "https://www.chiptuning.ai";

    public LoginWindow()
    {
        InitializeComponent();

        var session = SessionStore.Load();
        if (session != null && !string.IsNullOrEmpty(session.Email))
            EmailBox.Text = session.Email;
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth  = SystemParameters.WorkArea.Width;
            MaximizeBtn.Content = "❐";
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth  = double.PositiveInfinity;
            MaximizeBtn.Content = "☐";
        }
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Login_Click(sender, e);
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        LoginBtn.IsEnabled   = false;
        StartSpinner();

        try
        {
            var client = new ChiptuningAiClient(ApiUrl);
            await client.Auth.LoginAsync(EmailBox.Text.Trim(), PasswordBox.Password);

            SessionStore.Save(
                ApiUrl,
                EmailBox.Text.Trim(),
                client.AccessToken  ?? string.Empty,
                client.RefreshToken ?? string.Empty,
                client.TokenExpiresAt);

            var dashboard = new MainWindow(client, ApiUrl, EmailBox.Text.Trim());
            dashboard.Show();
            Close();
        }
        catch (ApiException ex)
        {
            var code = AppLogger.Error("Login failed", ex);
            ErrorText.Text       = $"Login failed. Please check your credentials. ({code})";
            ErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error("Login connection error", ex);
            ErrorText.Text       = $"Could not connect to the server. ({code})";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoginBtn.IsEnabled = true;
            StopSpinner();
        }
    }

    // ── Spinner ───────────────────────────────────────────────────────────────

    private void StartSpinner()
    {
        LoginBtnText.Visibility   = Visibility.Collapsed;
        LoginSpinner.Visibility   = Visibility.Visible;
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void StopSpinner()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LoginSpinner.Visibility  = Visibility.Collapsed;
        LoginBtnText.Visibility  = Visibility.Visible;
    }
}
