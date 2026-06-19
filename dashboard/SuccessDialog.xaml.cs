using System.Windows;
using System.Windows.Threading;

namespace ChiptuningAi.Dashboard;

public partial class SuccessDialog : Window
{
    private DispatcherTimer? _timer;
    private int _remaining = 5;

    public SuccessDialog(string message)
    {
        InitializeComponent();
        MessageText.Text    = message;
        CountdownText.Text  = "Closing in 5s";
        Loaded += (_, _) => StartCountdown();
    }

    private void StartCountdown()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            CountdownText.Text  = $"Closing in {_remaining}s";
            CountdownBar.Value  = _remaining / 5.0;
            if (_remaining <= 0) { _timer.Stop(); Close(); }
        };
        _timer.Start();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        Close();
    }
}
