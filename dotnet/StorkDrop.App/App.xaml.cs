using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StorkDrop.App.Views;
using StorkDrop.App.Views.SetupWizard;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry;

namespace StorkDrop.App;

public partial class App : Application
{
    private static readonly Mutex SingleInstanceMutex = new(true, "StorkDrop-SingleInstance-Mutex");
    private IHost? _host;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        string[] args = Environment.GetCommandLineArgs();

        if (args.Length >= 4 && args[1] == "--install")
        {
            RunElevatedInstall(args[2], args[3]);
            Shutdown();
            return;
        }

        if (args.Length >= 3 && args[1] == "--uninstall")
        {
            RunElevatedUninstall(args[2]);
            Shutdown();
            return;
        }

        if (!SingleInstanceMutex.WaitOne(TimeSpan.Zero, true))
        {
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

            LoadConfigIntoNexusOptions();

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

    private void RunElevatedInstall(string productId, string targetPath)
    {
        // Run entirely without SynchronizationContext to prevent deadlocks
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            LoadConfigIntoNexusOptions();

            IRegistryClient registryClient = Services.GetRequiredService<IRegistryClient>();
            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();

            ProductManifest? manifest = registryClient
                .GetProductManifestAsync(productId)
                .GetAwaiter()
                .GetResult();

            if (manifest is not null)
            {
                InstallOptions options = new(TargetPath: targetPath);
                Progress<InstallProgress> progress = new(_ => { });
                engine.InstallAsync(manifest, options, progress).GetAwaiter().GetResult();
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
                _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                _host?.Dispose();
            }
            catch { }
        }
    }

    private void RunElevatedUninstall(string productId)
    {
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            LoadConfigIntoNexusOptions();

            IInstallationEngine engine = Services.GetRequiredService<IInstallationEngine>();
            IProductRepository productRepository =
                Services.GetRequiredService<IProductRepository>();

            InstalledProduct? installed = productRepository
                .GetByIdAsync(productId)
                .GetAwaiter()
                .GetResult();

            if (installed is not null)
            {
                engine.UninstallAsync(installed).GetAwaiter().GetResult();
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
                _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                _host?.Dispose();
            }
            catch { }
        }
    }

    private static void LoadConfigIntoNexusOptions()
    {
        try
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StorkDrop",
                "Config"
            );
            string configPath = Path.Combine(configDir, "config.json");
            if (!File.Exists(configPath))
                return;

            string json = File.ReadAllText(configPath);
            AppConfiguration? config =
                System.Text.Json.JsonSerializer.Deserialize<AppConfiguration>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters =
                        {
                            new System.Text.Json.Serialization.JsonStringEnumConverter(),
                        },
                    }
                );

            if (config?.Feeds is not { Length: > 0 })
                return;

            FeedConfiguration firstFeed = config.Feeds[0];
            NexusOptions nexusOptions = Services.GetRequiredService<IOptions<NexusOptions>>().Value;
            nexusOptions.BaseUrl = firstFeed.Url;
            nexusOptions.Repository = firstFeed.Repository;

            if (!string.IsNullOrEmpty(firstFeed.Username))
            {
                nexusOptions.Username = firstFeed.Username;
                try
                {
                    if (!string.IsNullOrEmpty(firstFeed.EncryptedPassword))
                    {
                        IEncryptionService encryption =
                            Services.GetRequiredService<IEncryptionService>();
                        nexusOptions.Password = encryption.Decrypt(firstFeed.EncryptedPassword);
                    }
                }
                catch
                {
                    nexusOptions.Password = string.Empty;
                }
            }
        }
        catch
        {
            // Config load failure - use defaults
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
