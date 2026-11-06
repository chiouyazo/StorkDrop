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
            .WriteTo.Sink(new InstallLogSink())
            .CreateLogger();

        IHostBuilder builder = Host.CreateDefaultBuilder();
        builder.UseSerilog();
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        builder.ConfigureServices(
            (context, services) =>
            {
                services.AddSingleton<DemoControlState>();
                services.AddSingleton<DemoLockState>();
                services.AddSingleton<DemoFeedRegistry>();
                services.AddSingleton<IFeedRegistry>(p => p.GetRequiredService<DemoFeedRegistry>());
                services.AddSingleton<IInstallationEngine, DemoInstallationEngine>();
                services.AddSingleton<DemoProductRepository>();
                services.AddSingleton<IProductRepository>(p =>
                    p.GetRequiredService<DemoProductRepository>()
                );
                services.AddSingleton<IInstalledProductsSnapshot>(p =>
                    p.GetRequiredService<DemoProductRepository>()
                );
                services.AddSingleton<IConfigurationService, DemoConfigurationService>();
                services.AddSingleton<IActivityLog, DemoActivityLog>();
                services.AddSingleton<IBackupService, BackupService>();
                services.AddSingleton<IEncryptionService, DemoEncryptionService>();
                services.AddSingleton<IFileLockDetector, DemoFileLockDetector>();
                services.AddSingleton<ISelfUpdateChecker, DemoSelfUpdateChecker>();
                services.AddSingleton<INotificationService, DemoNotificationService>();
                services.AddSingleton<IPluginSettingsStore, DemoPluginSettingsStore>();
                services.AddSingleton<IFeedConnectionService, DemoFeedConnectionService>();
                services.AddSingleton<IIntegrityService, DemoIntegrityService>();
                services.AddSingleton<IFeedReportService, DemoFeedReportService>();

                services.AddSingleton<InstallationCoordinator>();
                services.AddSingleton<UninstallService>();
                services.AddSingleton<EnvironmentVariableService>();
                services.AddSingleton<DeferredFileOps>();
                services.AddSingleton<FileOperations>();
                services.AddSingleton<DependentUpdateResolver>();
                services.AddSingleton<IFeedLockService, FeedLockService>();
                services.AddHostedService<UpdateBackgroundService>();

                services.AddSingleton<IStorkDropPlugin, DemoStorkDropPlugin>();

                services.AddSingleton(
                    new PluginLoadStatus { TotalPluginDlls = 1, LoadedCount = 1 }
                );

                services.AddSingleton<NavigationService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<InstallationTracker>();
                services.AddSingleton<PostProductResolver>();
                services.AddSingleton<SelfUpdateService>();
                services.AddSingleton<DependentUpdateService>();

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
