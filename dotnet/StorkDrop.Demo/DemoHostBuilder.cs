using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StorkDrop.App;
using StorkDrop.App.Services;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Demo.Plugins;
using StorkDrop.Demo.Services;
using StorkDrop.Installer;
using StorkDrop.Registry;

namespace StorkDrop.Demo;

internal static class DemoHostBuilder
{
    public static IHost Build()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        IHostBuilder builder = Host.CreateDefaultBuilder();
        builder.UseSerilog();

        builder.ConfigureServices(
            (context, services) =>
            {
                services.AddSingleton<IFeedRegistry, DemoFeedRegistry>();
                services.AddSingleton<IInstallationEngine, DemoInstallationEngine>();
                services.AddSingleton<IProductRepository, DemoProductRepository>();
                services.AddSingleton<IConfigurationService, DemoConfigurationService>();
                services.AddSingleton<IActivityLog, DemoActivityLog>();
                services.AddSingleton<IBackupService, DemoBackupService>();
                services.AddSingleton<IEncryptionService, DemoEncryptionService>();
                services.AddSingleton<IFileLockDetector, DemoFileLockDetector>();
                services.AddSingleton<ISelfUpdateChecker, DemoSelfUpdateChecker>();
                services.AddSingleton<INotificationService, DemoNotificationService>();
                services.AddSingleton<IPluginSettingsStore, DemoPluginSettingsStore>();
                services.AddSingleton<IFeedConnectionService, DemoFeedConnectionService>();

                services.AddSingleton<InstallationCoordinator>();
                services.AddSingleton<UninstallService>();
                services.AddSingleton<EnvironmentVariableService>();
                services.AddSingleton<DeferredFileOps>();
                services.AddSingleton<FileOperations>();

                services.AddSingleton<IStorkDropPlugin, DemoStorkDropPlugin>();

                services.AddSingleton(
                    new PluginLoadStatus { TotalPluginDlls = 1, LoadedCount = 1 }
                );

                services.AddSingleton<NavigationService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<InstallationTracker>();
                services.AddSingleton<PostProductResolver>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MarketplaceViewModel>();
                services.AddSingleton<InstalledViewModel>();
                services.AddSingleton<UpdatesViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<ActivityLogViewModel>();
                services.AddSingleton<PluginsViewModel>();
                services.AddTransient<ProductDetailViewModel>();
                services.AddTransient<MainWindow>();
            }
        );

        return builder.Build();
    }
}
