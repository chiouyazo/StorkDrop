using System.Windows.Threading;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.App.Services;

/// <summary>
/// Notification service that uses the system tray balloon for notifications.
/// Thread-safe: dispatches to the UI thread if called from a background thread.
/// </summary>
public sealed class ToastNotificationService : INotificationService
{
    private readonly TrayIconService _trayIconService;

    public ToastNotificationService(TrayIconService trayIconService)
    {
        _trayIconService = trayIconService;
    }

    public void ShowInfo(string title, string message) => ShowBalloon(title, message);

    public void ShowSuccess(string title, string message) => ShowBalloon(title, message);

    public void ShowWarning(string title, string message) => ShowBalloon(title, message);

    public void ShowError(string title, string message) => ShowBalloon(title, message);

    public void ShowUpdateAvailable(string productTitle, string version) =>
        ShowBalloon("Update Available", $"{productTitle} v{version} is now available.");

    private void ShowBalloon(string title, string message)
    {
        Dispatcher dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            _trayIconService.ShowBalloon(title, message);
        }
        else
        {
            dispatcher.BeginInvoke(() => _trayIconService.ShowBalloon(title, message));
        }
    }
}
