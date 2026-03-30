using System.Windows;

namespace StorkDrop.App.Views;

public partial class UpdateNotificationDialog : Window
{
    public UpdateNotificationDialog(string version, string releaseNotes)
    {
        InitializeComponent();
        HeaderText.Text = $"StorkDrop {version} is available!";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(releaseNotes) ? "-" : releaseNotes;
    }

    private void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
