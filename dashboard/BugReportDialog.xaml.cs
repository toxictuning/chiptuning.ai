using System.Windows;
using ChiptuningAi.Client;

namespace ChiptuningAi.Dashboard;

public partial class BugReportDialog : Window
{
    private readonly ChiptuningAiClient _client;

    public BugReportDialog(ChiptuningAiClient client)
    {
        InitializeComponent();
        _client = client;
        TitleBox.Focus();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var title = TitleBox.Text.Trim();
        var desc  = DescriptionBox.Text.Trim();
        var steps = StepsBox.Text.Trim();

        if (string.IsNullOrEmpty(title))
        {
            ErrorText.Text = "Title is required.";
            ErrorText.Visibility = Visibility.Visible;
            TitleBox.Focus();
            return;
        }
        if (string.IsNullOrEmpty(desc))
        {
            ErrorText.Text = "Description is required.";
            ErrorText.Visibility = Visibility.Visible;
            DescriptionBox.Focus();
            return;
        }

        SubmitBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        SubmitBtn.Content   = "Submitting…";

        try
        {
            await _client.BugReports.SubmitAsync(
                title, desc,
                stepsToReproduce: string.IsNullOrEmpty(steps) ? null : steps);

            var ok = new SuccessDialog("Bug report submitted. Thank you — we'll look into it.");
            ok.Owner = this;
            ok.ShowDialog();
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            SubmitBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
            SubmitBtn.Content   = "SUBMIT";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
