using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.Installed;

public partial class InstalledView : UserControl
{
    public InstalledView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is InstalledViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"Fehler beim Laden: {ex.Message}");
        }
    }

    private void ShowNotification(string message)
    {
        NotificationText.Text = message;
        NotificationBanner.Visibility = Visibility.Visible;
    }
}
