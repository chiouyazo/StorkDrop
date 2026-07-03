using System.Windows;
using System.Windows.Controls;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.Contracts.Services;

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

    private void FeedPasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.Tag is FeedViewModel feed)
            passwordBox.Password = feed.Password;
    }

    private void FeedPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.Tag is FeedViewModel feed)
            feed.Password = passwordBox.Password;
    }

    private void ReportSecretBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.Tag is FeedViewModel feed)
            passwordBox.Password = feed.ReportSecret;
    }

    private void ReportSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.Tag is FeedViewModel feed)
            feed.ReportSecret = passwordBox.Password;
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

    private void LockCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not FeedViewModel feed)
            return;

        if (string.IsNullOrEmpty(feed.ExistingLockHash))
            return;

        FeedLockPromptDialog dialog = new FeedLockPromptDialog(
            feed.Name,
            LocalizationManager.GetString("FeedLock_Op_RemoveLock"),
            null
        )
        {
            Owner = Window.GetWindow(this),
        };

        bool authorized =
            dialog.ShowDialog() == true
            && PasswordHasher.Verify(dialog.EnteredPassword, feed.ExistingLockHash);

        if (authorized)
            feed.ExistingLockHash = null;
        else
            checkBox.IsChecked = true;
    }
}
