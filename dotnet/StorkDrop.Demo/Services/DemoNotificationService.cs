using System.Windows;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Demo.Ui;

namespace StorkDrop.Demo.Services;

internal sealed class DemoNotificationService : INotificationService
{
    public int UpdateNotificationCount { get; private set; }

    public void ShowInfo(string title, string message) => Show("Info", title, message);

    public void ShowSuccess(string title, string message) => Show("Success", title, message);

    public void ShowWarning(string title, string message) => Show("Warning", title, message);

    public void ShowError(string title, string message) => Show("Error", title, message);

    public void ShowUpdateAvailable(string productTitle, string version)
    {
        UpdateNotificationCount++;
        Show(
            "Update available",
            $"Update Available (notification #{UpdateNotificationCount})",
            $"{productTitle} v{version} is now available."
        );
    }

    private static void Show(string kind, string title, string message)
    {
        Application? app = Application.Current;
        app?.Dispatcher.BeginInvoke(() => DemoToastWindow.ShowToast(kind, title, message));
    }
}
