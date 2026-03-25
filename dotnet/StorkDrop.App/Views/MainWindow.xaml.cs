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
        AppConfiguration? config = configService.LoadAsync().GetAwaiter().GetResult();

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

            // Feature 4: First time minimizing to tray, show toast notification
            if (config is not null && !config.HasShownTrayToast)
            {
                trayService.ShowBalloon(
                    LocalizationManager.GetString("Tray_Title"),
                    LocalizationManager.GetString("Tray_FirstMinimize")
                );

                // Save that we've shown the toast
                AppConfiguration updatedConfig = config with
                {
                    HasShownTrayToast = true,
                };
                configService.SaveAsync(updatedConfig).GetAwaiter().GetResult();
            }
        }
        else
        {
            Application.Current.Shutdown();
        }

        base.OnClosing(e);
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
