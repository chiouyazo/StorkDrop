using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using StorkDrop.App.Services;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.App.Views.SetupWizard;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Installer;
using StorkDrop.Registry;

namespace StorkDrop.App;

/// <summary>
/// Tracks plugin loading statistics.
/// </summary>
public sealed class PluginLoadStatus
{
    /// <summary>
    /// Gets or sets the total number of plugin DLLs discovered.
    /// </summary>
    public int TotalPluginDlls { get; set; }

    /// <summary>
    /// Gets or sets the number of plugins that loaded successfully.
    /// </summary>
    public int LoadedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of plugins that failed to load.
    /// </summary>
    public int FailedCount { get; set; }
}

/// <summary>
/// Configures and builds the application host with all required services.
/// </summary>
public static class AppHostBuilder
{
    /// <summary>
    /// Builds the application host with dependency injection, Serilog logging, and plugin discovery.
    /// </summary>
    /// <returns>The configured <see cref="IHost"/> instance.</returns>
    public static IHost Build()
    {
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StorkDrop",
            "Logs",
            "storkdrop-.log"
        );

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        IHostBuilder builder = Host.CreateDefaultBuilder();

        builder.UseSerilog();

        builder.ConfigureServices(
            (context, services) =>
            {
                services.AddInstaller();

                // Register with defaults - App.xaml.cs updates options after async config load
                services.AddNexusRegistry(options =>
                {
                    options.BaseUrl = "https://example.com";
                    options.Repository = "releases";
                });

                services.AddSingleton<NavigationService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<INotificationService, ToastNotificationService>();
                services.AddHostedService<UpdateBackgroundService>();

                // Auto-load plugins from plugins/ directory
                PluginLoadStatus pluginLoadStatus = new PluginLoadStatus();
                LoadPlugins(services, pluginLoadStatus);
                services.AddSingleton(pluginLoadStatus);

                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<SetupWizardViewModel>();
                services.AddSingleton<MarketplaceViewModel>();
                services.AddSingleton<InstalledViewModel>();
                services.AddSingleton<UpdatesViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<ActivityLogViewModel>();
                services.AddSingleton<PluginsViewModel>();
                services.AddTransient<ProductDetailViewModel>();
                services.AddTransient<TrayIconViewModel>();

                services.AddTransient<MainWindow>();
                services.AddTransient<SetupWizardWindow>();
            }
        );

        return builder.Build();
    }

    private static void LoadPlugins(IServiceCollection services, PluginLoadStatus status)
    {
        string exeDirectory = AppContext.BaseDirectory;
        string pluginsDirectory = Path.Combine(exeDirectory, "plugins");

        if (!Directory.Exists(pluginsDirectory))
            return;

        string[] dllFiles = Directory.GetFiles(pluginsDirectory, "*.dll");
        status.TotalPluginDlls = dllFiles.Length;

        foreach (string dllPath in dllFiles)
        {
            try
            {
                AssemblyLoadContext loadContext = new AssemblyLoadContext(
                    Path.GetFileNameWithoutExtension(dllPath),
                    isCollectible: false
                );
                Assembly assembly = loadContext.LoadFromAssemblyPath(dllPath);

                IEnumerable<Type> pluginTypes = assembly
                    .GetTypes()
                    .Where(t =>
                        typeof(IStorkDropPlugin).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false }
                    );

                bool foundPlugin = false;
                foreach (Type pluginType in pluginTypes)
                {
                    services.AddSingleton(typeof(IStorkDropPlugin), pluginType);
                    foundPlugin = true;
                }

                if (foundPlugin)
                {
                    status.LoadedCount++;
                }
            }
            catch
            {
                status.FailedCount++;
            }
        }
    }
}
