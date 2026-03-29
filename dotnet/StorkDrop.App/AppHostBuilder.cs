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
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using StorkDrop.Registry;

namespace StorkDrop.App;

/// <summary>
/// Tracks plugin loading statistics.
/// </summary>
public sealed class PluginLoadStatus
{
    public int TotalPluginDlls { get; set; }
    public int LoadedCount { get; set; }
    public int FailedCount { get; set; }
    public List<PluginLoadError> Errors { get; } = [];
}

public sealed class PluginLoadError
{
    public string DllPath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
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
        string logPath = StorkPaths.LogFile;

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

                services.AddFeedRegistry();

                services.AddSingleton<NavigationService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<INotificationService, ToastNotificationService>();
                services.AddHostedService<UpdateBackgroundService>();
                services.AddSingleton<InstallationTracker>();

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
        List<string> pluginDirs = [Path.Combine(AppContext.BaseDirectory, "plugins")];

        // Support --plugin-dir for development/debugging
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--plugin-dir" && Directory.Exists(args[i + 1]))
                pluginDirs.Add(args[i + 1]);
        }

        List<string> allDlls = [];
        foreach (string dir in pluginDirs)
        {
            if (Directory.Exists(dir))
                allDlls.AddRange(Directory.GetFiles(dir, "*.dll"));
        }

        string[] dllFiles = allDlls.ToArray();
        status.TotalPluginDlls = dllFiles.Length;

        foreach (string dllPath in dllFiles)
        {
            string dllName = Path.GetFileName(dllPath);
            Log.Information("Loading plugin DLL: {DllPath}", dllPath);

            try
            {
                PluginLoadContext loadContext = new PluginLoadContext(dllPath);
                Assembly assembly = loadContext.LoadFromAssemblyPath(dllPath);

                Type[] allTypes;
                try
                {
                    allTypes = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    string loaderErrors = string.Join(
                        "; ",
                        ex.LoaderExceptions?.Where(e => e is not null).Select(e => e!.Message) ?? []
                    );
                    string error = $"Could not load types: {loaderErrors}";
                    Log.Error("Plugin {DllName} failed: {Error}", dllName, error);
                    status.FailedCount++;
                    status.Errors.Add(
                        new PluginLoadError { DllPath = dllName, ErrorMessage = error }
                    );
                    continue;
                }

                IEnumerable<Type> pluginTypes = allTypes.Where(t =>
                    typeof(IStorkDropPlugin).IsAssignableFrom(t)
                    && t is { IsInterface: false, IsAbstract: false }
                );

                bool foundPlugin = false;
                foreach (Type pluginType in pluginTypes)
                {
                    services.AddSingleton(typeof(IStorkDropPlugin), pluginType);
                    Log.Information(
                        "Registered plugin {PluginType} from {DllName}",
                        pluginType.FullName,
                        dllName
                    );
                    foundPlugin = true;
                }

                if (foundPlugin)
                {
                    status.LoadedCount++;
                }
                else
                {
                    Log.Warning(
                        "DLL {DllName} loaded but contains no IStorkDropPlugin implementations",
                        dllName
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Plugin {DllName} failed to load", dllName);
                status.FailedCount++;
                status.Errors.Add(
                    new PluginLoadError { DllPath = dllName, ErrorMessage = ex.Message }
                );
            }
        }
    }

    /// <summary>
    /// Custom AssemblyLoadContext that resolves shared assemblies (like StorkDrop.Contracts)
    /// from the host app instead of requiring an exact version match in the plugin directory.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // For shared assemblies, use the host's version (avoiding mismatch)
            Assembly? alreadyLoaded = Default.Assemblies.FirstOrDefault(a =>
                string.Equals(
                    a.GetName().Name,
                    assemblyName.Name,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (alreadyLoaded is not null)
                return alreadyLoaded;

            // Try to resolve from the plugin's own directory
            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolvedPath is not null)
                return LoadFromAssemblyPath(resolvedPath);

            return null;
        }
    }
}
