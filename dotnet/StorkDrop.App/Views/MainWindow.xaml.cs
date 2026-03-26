using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.App.ViewModels;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Initialize(ContentRegion);

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionRun.Text = $"StorkDrop v{version}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        IConfigurationService configService =
            App.Services.GetRequiredService<IConfigurationService>();

        // Must be synchronous — WPF checks e.Cancel immediately on return.
        // Use ConfigureAwait(false) via Task.Run to avoid SyncContext deadlock.
        AppConfiguration? config = Task.Run(async () => await configService.LoadAsync())
            .GetAwaiter()
            .GetResult();

        bool minimizeToTray = config?.AutoCheckForUpdates == true;

        if (minimizeToTray)
        {
            e.Cancel = true;
            Hide();

            TrayIconService trayService = App.Services.GetRequiredService<TrayIconService>();
            trayService.Show(
                onOpen: () =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                },
                onExit: () =>
                {
                    trayService.Hide();
                    Application.Current.Shutdown();
                }
            );

            if (config is not null && !config.HasShownTrayToast)
            {
                trayService.ShowBalloon(
                    LocalizationManager.GetString("Tray_Title"),
                    LocalizationManager.GetString("Tray_FirstMinimize")
                );

                AppConfiguration updatedConfig = config with { HasShownTrayToast = true };
                // Fire-and-forget — closing shouldn't block on saving the toast flag
                _ = configService.SaveAsync(updatedConfig);
            }
        }
        else
        {
            Application.Current.Shutdown();
        }

        base.OnClosing(e);
    }

    private void OnInstallationItemClick(object sender, MouseButtonEventArgs e)
    {
        if (
            sender is FrameworkElement { DataContext: Services.TrackedInstallation install }
            && DataContext is ViewModels.MainWindowViewModel vm
        )
        {
            vm.SelectedInstallation = install;

            // Show a log window
            Views.InstallLogWindow logWindow = new(install) { Owner = this };
            logWindow.Show();
        }
    }

    private void OnVersionClick(object sender, MouseButtonEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo("https://github.com/chiouyazo/StorkDrop")
            {
                UseShellExecute = true,
            }
        );
    }
}
