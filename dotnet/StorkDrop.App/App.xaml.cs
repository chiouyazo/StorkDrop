using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StorkDrop.App.Views;
using StorkDrop.App.Views.SetupWizard;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App;

public partial class App : Application
{
    private static readonly Mutex SingleInstanceMutex = new(true, "StorkDrop-SingleInstance-Mutex");
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

            // Wire up engine callbacks
            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();

            // File handler config dialog
            engine.OnFileHandlerConfigNeeded = (fields, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                Dispatcher.Invoke(() =>
                {
                    ViewModels.PluginConfigDialogViewModel vm = new(fields, currentValues);
                    Views.PluginConfigDialog dialog = new() { DataContext = vm };
                    dialog.Owner = MainWindow;
                    if (dialog.ShowDialog() == true)
                        result = vm.GetValues();
                });
                return result;
            };

            // Install path resolution via plugins (e.g., {ACMEPath} -> actual directory)
            IEnumerable<IStorkDropPlugin> allPlugins = Services.GetServices<IStorkDropPlugin>();
            List<StorkDrop.Contracts.IInstallPathResolver> pathResolvers = allPlugins
                .OfType<StorkDrop.Contracts.IInstallPathResolver>()
                .ToList();
            if (pathResolvers.Count > 0)
            {
                engine.OnResolveInstallPath = (targetPath, context) =>
                {
                    foreach (StorkDrop.Contracts.IInstallPathResolver resolver in pathResolvers)
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

    private async Task RunElevatedInstallAsync(string productId, string targetPath, string feedId)
    {
        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();
            IRegistryClient registryClient = feedRegistry.GetClient(feedId);

            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();

            ProductManifest? manifest = await registryClient.GetProductManifestAsync(productId);

            if (manifest is not null)
            {
                InstallOptions options = new(
                    TargetPath: targetPath,
                    FeedId: feedId,
                    SkipFileHandlers: true
                );
                Progress<InstallProgress> progress = new(_ => { });
                await engine.InstallAsync(manifest, options, progress);
            }

            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Elevated install failed: {ex.Message}");
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

    private async Task RunElevatedUninstallAsync(string productId)
    {
        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();

            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();
            IProductRepository productRepository =
                Services.GetRequiredService<IProductRepository>();

            InstalledProduct? installed = await productRepository.GetByIdAsync(productId);

            if (installed is not null)
            {
                await engine.UninstallAsync(installed);
            }

            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Elevated uninstall failed: {ex.Message}");
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

    private async Task RunElevatedUpdateAsync(string productId, string targetPath, string feedId)
    {
        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();
            IRegistryClient registryClient = feedRegistry.GetClient(feedId);

            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();
            IProductRepository productRepository =
                Services.GetRequiredService<IProductRepository>();

            InstalledProduct? installed = await productRepository.GetByIdAsync(productId);

            ProductManifest? manifest = await registryClient.GetProductManifestAsync(productId);

            if (installed is not null && manifest is not null)
            {
                InstallOptions options = new(TargetPath: targetPath, FeedId: feedId);
                Progress<InstallProgress> progress = new(_ => { });
                await engine.UpdateAsync(installed, manifest, options, progress);
            }

            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Elevated update failed: {ex.Message}");
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
