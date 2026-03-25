using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;

namespace StorkDrop.App.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                await viewModel.LoadAsync();
            }
        }
        catch (Exception ex)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.ErrorMessage = LocalizationManager.GetString(
                    "Error_LoadFailed",
                    ex.Message
                );
            }
        }
    }
}
