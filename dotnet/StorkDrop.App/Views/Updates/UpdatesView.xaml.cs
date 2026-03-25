using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.Updates;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
    }

    private void OnToggleReleaseNotes(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UpdateItemViewModel item })
            item.ShowReleaseNotes = !item.ShowReleaseNotes;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is UpdatesViewModel viewModel)
            {
                await viewModel.LoadAsync();
            }
        }
        catch (Exception ex)
        {
            if (DataContext is UpdatesViewModel viewModel)
            {
                viewModel.ErrorMessage = $"Fehler beim Laden: {ex.Message}";
            }
        }
    }
}
