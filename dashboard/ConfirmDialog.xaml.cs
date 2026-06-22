using System.Windows;
using System.Windows.Media;

namespace ChiptuningAi.Dashboard;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog(string message, string? subMessage = null, bool isDanger = false, string confirmLabel = "Confirm")
    {
        InitializeComponent();
        MessageText.Text    = message;
        ConfirmBtn.Content  = confirmLabel;

        if (subMessage is not null)
        {
            SubText.Text       = subMessage;
            SubText.Visibility = Visibility.Visible;
        }

        if (isDanger)
        {
            IconText.Text       = "⚠";
            IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            TitleText.Text      = "WARNING";
            TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            ConfirmBtn.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
