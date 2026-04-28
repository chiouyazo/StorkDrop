using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using StorkDrop.App.Localization;

namespace StorkDrop.App.Services;

public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;

    public bool IsVisible => _trayIcon?.Visibility == Visibility.Visible;

    public void Show(Action onOpen, Action onExit)
    {
        if (_trayIcon is not null)
            return;

        MenuItem openItem = new MenuItem { Header = LocalizationManager.GetString("Tray_Open") };
        openItem.Click += (_, _) => onOpen();

        MenuItem exitItem = new MenuItem { Header = LocalizationManager.GetString("Tray_Exit") };
        exitItem.Click += (_, _) => onExit();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "StorkDrop",
            ContextMenu = new ContextMenu { Items = { openItem, new Separator(), exitItem } },
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => onOpen();

        _trayIcon.TrayMouseDoubleClick += (_, _) => onOpen();

        try
        {
            using System.IO.Stream? stream = Application
                .GetResourceStream(
                    new Uri(
                        "pack://application:,,,/StorkDrop.App;component/Assets/stork_icon_32.png"
                    )
                )
                ?.Stream;
            if (stream is not null)
            {
                Bitmap bitmap = new Bitmap(stream);
                _trayIcon.Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
            }
        }
        catch
        {
            // Use default icon if resource not found
        }

        _trayIcon.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        if (_trayIcon is null)
            return;

        if (_trayIcon.Dispatcher.CheckAccess())
        {
            _trayIcon.Visibility = Visibility.Collapsed;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        else
        {
            _trayIcon.Dispatcher.Invoke(() =>
            {
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon.Dispose();
                _trayIcon = null;
            });
        }
    }

    public void ShowBalloon(string title, string message)
    {
        _trayIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);
    }

    public void Dispose()
    {
        try
        {
            Hide();
        }
        catch
        {
            // Dispatcher may already be shut down during app exit
        }
    }
}
