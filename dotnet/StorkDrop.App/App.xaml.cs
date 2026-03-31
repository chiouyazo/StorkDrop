using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StorkDrop.App.Services;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.App.Views.SetupWizard;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App;

public partial class App : Application
{
    private static readonly Mutex SingleInstanceMutex = new Mutex(
        true,
        "StorkDrop-SingleInstance-Mutex"
    );
    private IHost? _host;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        string[] args = Environment.GetCommandLineArgs();

        if (args.Length >= 5 && args[1] == "--install")
        {
            await RunElevatedInstallAsync(args[2], args[3], args[4]);
            Shutdown();
            return;
        }

        if (args.Length >= 3 && args[1] == "--uninstall")
        {
            await RunElevatedUninstallAsync(args[2]);
            Shutdown();
            return;
        }

        if (args.Length >= 5 && args[1] == "--update")
        {
            await RunElevatedUpdateAsync(args[2], args[3], args[4]);
            Shutdown();
            return;
        }

        if (!SingleInstanceMutex.WaitOne(TimeSpan.Zero, true))
        {
            MessageBox.Show(
                "StorkDrop is already running. Check the system tray.",
                "StorkDrop",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;

            SynchronizationContext? savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                _host.Start();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(savedContext);
            }

            IConfigurationService configService =
                Services.GetRequiredService<IConfigurationService>();

            if (!configService.ConfigurationExists())
            {
                SetupWizardWindow wizard = Services.GetRequiredService<SetupWizardWindow>();
                bool? result = wizard.ShowDialog();
                if (result != true)
                {
                    Shutdown();
                    return;
                }
            }

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();

            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();

            engine.OnFileHandlerConfigNeeded = (fields, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                Dispatcher.Invoke(() =>
                {
                    ViewModels.PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                        fields,
                        currentValues
                    );
                    Views.PluginConfigDialog dialog = new PluginConfigDialog { DataContext = vm };
                    dialog.Owner = MainWindow;
                    if (dialog.ShowDialog() == true)
                        result = vm.GetValues();
                });
                return result;
            };

            engine.OnPluginConfigNeeded = (fields, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                Dispatcher.Invoke(() =>
                {
                    ViewModels.PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                        fields,
                        currentValues
                    );
                    Views.PluginConfigDialog dialog = new PluginConfigDialog { DataContext = vm };
                    dialog.Owner = MainWindow;
                    if (dialog.ShowDialog() == true)
                        result = vm.GetValues();
                });
                return result;
            };

            // Install path resolution via plugins (e.g., {ACMEPath} -> actual directory)
            IEnumerable<IStorkDropPlugin> allPlugins = Services.GetServices<IStorkDropPlugin>();
            List<IInstallPathResolver> pathResolvers = allPlugins
                .OfType<IInstallPathResolver>()
                .ToList();
            if (pathResolvers.Count > 0)
            {
                engine.OnResolveInstallPath = (targetPath, context) =>
                {
                    foreach (IInstallPathResolver resolver in pathResolvers)
                    {
                        string? resolved = resolver.ResolveInstallPath(targetPath, context);
                        if (resolved is not null)
                            return resolved;
                    }
                    return null;
                };
            }

            MainWindow mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Fire-and-forget self-update check
            _ = CheckForSelfUpdateAsync(mainWindow);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"StorkDrop could not be started:\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown();
        }
    }

    private async Task CheckForSelfUpdateAsync(Window owner)
    {
        try
        {
            IConfigurationService configService =
                Services.GetRequiredService<IConfigurationService>();
            AppConfiguration? config = await configService.LoadAsync();
            if (config is null || !config.CheckForStorkDropUpdates)
                return;

            ISelfUpdateChecker checker = Services.GetRequiredService<ISelfUpdateChecker>();
            UpdateInfo? update = await checker.CheckForUpdateAsync(config.IncludeDevVersions);
            if (update is null)
                return;

            bool shouldUpdate = Dispatcher.Invoke(() =>
            {
                Views.UpdateNotificationDialog dialog = new(
                    update.Version,
                    update.ReleaseNotes ?? ""
                )
                {
                    Owner = owner,
                };
                return dialog.ShowDialog() == true;
            });

            if (shouldUpdate)
            {
                SelfUpdateService updateService = Services.GetRequiredService<SelfUpdateService>();

                await Dispatcher.InvokeAsync(() =>
                {
                    owner.IsEnabled = false;
                    owner.Title = $"StorkDrop - Downloading update v{update.Version}...";
                });

                try
                {
                    await updateService.DownloadAndLaunchInstallerAsync(update);
                }
                catch (Exception dlEx)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        owner.IsEnabled = true;
                        owner.Title = "StorkDrop";
                        MessageBox.Show(
                            $"Update download failed: {dlEx.Message}",
                            "Update Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Self-update check failed: {ex.Message}");
        }
    }

    private Task RunElevatedInstallAsync(string productId, string targetPath, string feedId)
    {
        Dictionary<string, string>? configValues = LoadElevationConfigFile();

        return RunElevatedAsync(
            "install",
            async services =>
            {
                IFeedRegistry feedRegistry = services.GetRequiredService<IFeedRegistry>();
                IRegistryClient registryClient = feedRegistry.GetClient(feedId);
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();

                ProductManifest? manifest = await registryClient.GetProductManifestAsync(productId);
                if (manifest is not null)
                {
                    InstallOptions options = new InstallOptions(
                        TargetPath: targetPath,
                        FeedId: feedId,
                        SkipFileHandlers: true,
                        PluginConfigValues: configValues
                    );
                    Progress<InstallProgress> progress = new Progress<InstallProgress>(_ => { });
                    await engine.InstallAsync(manifest, options, progress);
                }
            }
        );
    }

    private Task RunElevatedUninstallAsync(string productId)
    {
        return RunElevatedAsync(
            "uninstall",
            async services =>
            {
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();
                IProductRepository productRepository =
                    services.GetRequiredService<IProductRepository>();

                InstalledProduct? installed = await productRepository.GetByIdAsync(productId);
                if (installed is not null)
                    await engine.UninstallAsync(installed);
            }
        );
    }

    private Task RunElevatedUpdateAsync(string productId, string targetPath, string feedId)
    {
        return RunElevatedAsync(
            "update",
            async services =>
            {
                IFeedRegistry feedRegistry = services.GetRequiredService<IFeedRegistry>();
                IRegistryClient registryClient = feedRegistry.GetClient(feedId);
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();
                IProductRepository productRepository =
                    services.GetRequiredService<IProductRepository>();

                InstalledProduct? installed = await productRepository.GetByIdAsync(productId);
                ProductManifest? manifest = await registryClient.GetProductManifestAsync(productId);

                if (installed is not null && manifest is not null)
                {
                    InstallOptions options = new InstallOptions(
                        TargetPath: targetPath,
                        FeedId: feedId
                    );
                    Progress<InstallProgress> progress = new Progress<InstallProgress>(_ => { });
                    await engine.UpdateAsync(installed, manifest, options, progress);
                }
            }
        );
    }

    private async Task RunElevatedAsync(string operation, Func<IServiceProvider, Task> action)
    {
        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();

            await action(Services);
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Elevated {operation} failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
        finally
        {
            try
            {
                if (_host is not null)
                    await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host?.Dispose();
            }
            catch { }
        }
    }

    private static Dictionary<string, string>? LoadElevationConfigFile()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config-file")
            {
                string path = args[i + 1];
                try
                {
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        File.Delete(path);
                        return System.Text.Json.JsonSerializer.Deserialize<
                            Dictionary<string, string>
                        >(json);
                    }
                }
                catch { }
                break;
            }
        }
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                Task stopTask = _host.StopAsync(TimeSpan.FromSeconds(3));
                stopTask.ContinueWith(_ => _host.Dispose(), TaskScheduler.Default);
            }

            SingleInstanceMutex.ReleaseMutex();
        }
        catch
        {
            // Swallow on exit
        }

        base.OnExit(e);
    }
}
