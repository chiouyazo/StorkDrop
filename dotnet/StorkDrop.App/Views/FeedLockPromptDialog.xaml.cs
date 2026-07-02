using System.Windows;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Views;

/// <summary>
/// Modal prompt asking for a feed's soft-lock password before a mutating operation.
/// </summary>
public partial class FeedLockPromptDialog : Window
{
    /// <summary>The password entered by the user (only meaningful when DialogResult is true).</summary>
    public string EnteredPassword { get; private set; } = string.Empty;

    public FeedLockPromptDialog(string feedName, string operationName, string? errorMessage)
    {
        InitializeComponent();

        MessageText.Text = LocalizationManager
            .GetString("FeedLock_Message")
            .Replace("{0}", operationName)
            .Replace("{1}", feedName);

        if (!string.IsNullOrEmpty(errorMessage))
        {
            ErrorText.Text = errorMessage;
            ErrorText.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        EnteredPassword = PasswordInput.Password;
        DialogResult = true;
        Close();
    }
}
