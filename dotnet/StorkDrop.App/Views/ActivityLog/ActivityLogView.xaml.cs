using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.ActivityLog;

public partial class ActivityLogView : UserControl
{
    public ActivityLogView()
    {
        InitializeComponent();
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
                viewModel.ErrorMessage = $"Fehler beim Laden: {ex.Message}";
            }
        }
    }
}
