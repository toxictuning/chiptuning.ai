using System.Windows;

namespace ChiptuningAi.Dashboard;

public partial class RenameDialog : Window
{
    public string? NewDescription { get; private set; }
    public string? NewVersion     { get; private set; }

    public RenameDialog()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        NewDescription = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        NewVersion     = string.IsNullOrWhiteSpace(VersionBox.Text)     ? null : VersionBox.Text.Trim();
        DialogResult   = true;
    }
}
