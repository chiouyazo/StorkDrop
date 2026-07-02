using System.Windows;
using System.Windows.Controls;
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
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }

    private void LockPasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
            passwordBox.Password = string.Empty;
    }

    private void LockPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.Tag is FeedViewModel feed)
            feed.LockPassword = passwordBox.Password;
    }
}
