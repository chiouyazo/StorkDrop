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
        if (DataContext is ActivityLogViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
