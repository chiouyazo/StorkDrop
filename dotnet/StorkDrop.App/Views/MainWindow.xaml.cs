using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.App.ViewModels;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Initialize(ContentRegion);

        ApplyBranding();

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionRun.Text = $"StorkDrop v{version}";
    }

    private void ApplyBranding()
    {
        BrandingInfo branding = Branding.Current;
        Title = branding.WindowTitle;

        if (!branding.IsBranded)
            return;

        PoweredBy.Visibility = Visibility.Visible;

        BitmapImage? logo = LoadLogo(branding.LogoPath);
        if (logo is not null)
        {
            HeaderLogo.Source = logo;
            HeaderTitle.Visibility = Visibility.Collapsed;
        }
        else if (!string.IsNullOrWhiteSpace(branding.DisplayName))
        {
            HeaderTitle.Text = branding.DisplayName;
        }
    }

    private static BitmapImage? LoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        BitmapImage logo = new BitmapImage();
        logo.BeginInit();
        logo.CacheOption = BitmapCacheOption.OnLoad;
        logo.UriSource = new Uri(path, UriKind.Absolute);
        logo.EndInit();
        logo.Freeze();
        return logo;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ShouldCancelForActiveInstallations())
        {
            e.Cancel = true;
            return;
        }

        TrayIconService? trayService = App.Services.GetService<TrayIconService>();
        if (trayService?.IsVisible == true && ShouldRunInBackground())
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            trayService?.Hide();
            Application.Current.Shutdown();
        }

        base.OnClosing(e);
    }

    private static bool ShouldRunInBackground()
    {
        try
        {
            IConfigurationService configService =
                App.Services.GetRequiredService<IConfigurationService>();
            AppConfiguration? config = configService.LoadAsync().GetAwaiter().GetResult();
            return config?.RunInBackground ?? true;
        }
        catch
        {
            return true;
        }
    }

    private static bool ShouldCancelForActiveInstallations()
    {
        InstallationTracker tracker = App.Services.GetRequiredService<InstallationTracker>();
        if (!tracker.HasActiveInstallations)
            return false;

        string message = LocalizationManager
            .GetString("Closing_ActiveInstallations")
            .Replace("{0}", tracker.ActiveCount.ToString());
        string title = LocalizationManager.GetString("Closing_ActiveInstallations_Title");

        MessageBoxResult result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        return result != MessageBoxResult.Yes;
    }

    private void MinimizeToTray(AppConfiguration config, IConfigurationService configService)
    {
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

        if (!config.HasShownTrayToast)
        {
            trayService.ShowBalloon(
                LocalizationManager.GetString("Tray_Title"),
                LocalizationManager.GetString("Tray_FirstMinimize")
            );

            AppConfiguration updatedConfig = config with { HasShownTrayToast = true };
            // Fire-and-forget; closing shouldn't block on saving the toast flag
            _ = configService.SaveAsync(updatedConfig);
        }
    }

    private void OnInstallationItemClick(object sender, MouseButtonEventArgs e)
    {
        if (
            sender is FrameworkElement { DataContext: Services.TrackedInstallation install }
            && DataContext is ViewModels.MainWindowViewModel vm
        )
        {
            vm.SelectedInstallation = install;

            Views.InstallLogWindow logWindow = new InstallLogWindow(install) { Owner = this };
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
