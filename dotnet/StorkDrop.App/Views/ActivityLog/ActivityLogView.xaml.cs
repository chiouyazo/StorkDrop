using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.ActivityLog;

public partial class ActivityLogView : UserControl
{
    public ActivityLogView()
    {
        InitializeComponent();
    }

    private void OnViewLogs(object sender, RoutedEventArgs e)
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StorkDrop",
            "Logs"
        );

        if (Directory.Exists(logDir))
        {
            Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ActivityLogViewModel viewModel)
            {
                await viewModel.LoadAsync();
            }
        }
        catch (Exception ex)
        {
            if (DataContext is ActivityLogViewModel viewModel)
            {
                viewModel.ErrorMessage = LocalizationManager.GetString(
                    "Error_LoadFailed",
                    ex.Message
                );
            }
        }
    }
}
