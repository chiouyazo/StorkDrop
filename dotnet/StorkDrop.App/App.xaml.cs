using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StorkDrop.App.Services;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.App.Views.SetupWizard;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Log = Serilog.Log;

namespace StorkDrop.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showWindowEvent;
    private IHost? _host;

    public static IServiceProvider Services { get; internal set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        Branding.Initialize(WhitelabelConfig.Load(AppContext.BaseDirectory));

        string[] args = Environment.GetCommandLineArgs();

        if (args.Length >= 5 && args[1] == "--install")
        {
            string installInstanceId =
                GetArgValue(args, "--instance") ?? InstanceIdHelper.DefaultInstanceId;
            string installVersion = GetArgValue(args, "--version") ?? "";
            await RunElevatedInstallAsync(
                args[2],
                args[3],
                args[4],
                installInstanceId,
                installVersion
            );
            Shutdown();
            return;
        }

        if (args.Length >= 3 && args[1] == "--uninstall")
        {
            string uninstallInstanceId =
                GetArgValue(args, "--instance") ?? InstanceIdHelper.DefaultInstanceId;
            await RunElevatedUninstallAsync(args[2], uninstallInstanceId);
            Shutdown();
            return;
        }

        if (args.Length >= 5 && args[1] == "--update")
        {
            string updateInstanceId =
                GetArgValue(args, "--instance") ?? InstanceIdHelper.DefaultInstanceId;
            string updateVersion = GetArgValue(args, "--version") ?? "";
            await RunElevatedUpdateAsync(
                args[2],
                args[3],
                args[4],
                updateInstanceId,
                updateVersion
            );
            Shutdown();
            return;
        }

        if (args.Length >= 3 && args[1] == "--reexecute")
        {
            string reExecuteInstanceId =
                GetArgValue(args, "--instance") ?? InstanceIdHelper.DefaultInstanceId;
            await RunElevatedReExecuteAsync(args[2], reExecuteInstanceId);
            Shutdown();
            return;
        }

        if (args.Length >= 2 && args[1] == "--cli")
        {
            await RunCliModeAsync(args);
            Shutdown();
            return;
        }

        string instanceScope = Branding.Current.AppFolderName;
        _singleInstanceMutex = new Mutex(true, $"{instanceScope}-SingleInstance-Mutex");
        _showWindowEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"{instanceScope}-ShowWindow-Event"
        );

        if (!_singleInstanceMutex.WaitOne(TimeSpan.Zero, true))
        {
            // Signal the already-running instance to show its window
            _showWindowEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Environment.SetEnvironmentVariable("DOTNET_DbgEnableMiniDump", "1");
        Environment.SetEnvironmentVariable("DOTNET_DbgMiniDumpType", "2");
        Environment.SetEnvironmentVariable(
            "DOTNET_DbgMiniDumpName",
            Path.Combine(StorkPaths.LogDir, "crash-%p-%e.dmp")
        );

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled exception on UI thread");
            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled domain exception");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
        };

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

            AppConfiguration? existingConfig = await configService.LoadAsync();
            if (existingConfig is not null)
                Localization.LocalizationManager.Initialize(existingConfig.Language);

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
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                            fields,
                            currentValues
                        );
                        Views.PluginConfigDialog dialog = new PluginConfigDialog
                        {
                            DataContext = vm,
                        };
                        dialog.Owner = MainWindow;
                        if (dialog.ShowDialog() == true)
                            result = vm.GetValues();
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "File handler config dialog failed");
                }
                return result;
            };

            engine.OnPluginConfigNeeded = (fields, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        ViewModels.PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                            fields,
                            currentValues
                        );
                        vm.InteractivePlugin = engine.CurrentInteractivePlugin;
                        Views.PluginConfigDialog dialog = new PluginConfigDialog
                        {
                            DataContext = vm,
                        };
                        dialog.Owner = MainWindow;
                        if (dialog.ShowDialog() == true)
                            result = vm.GetValues();
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Plugin config dialog failed");
                }
                return result;
            };

            engine.OnActionGroupConfigNeeded = (groups, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        ViewModels.PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                            groups,
                            currentValues
                        );
                        vm.InteractivePlugin = engine.CurrentInteractivePlugin;
                        Views.PluginConfigDialog dialog = new PluginConfigDialog
                        {
                            DataContext = vm,
                        };
                        dialog.Owner = MainWindow;
                        if (dialog.ShowDialog() == true)
                            result = vm.GetValues();
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Action group config dialog failed");
                }
                return result;
            };

            LockedFilesCallback lockedFilesHandler = CreateLockedFilesHandler();

            engine.OnLockedFilesDetected = lockedFilesHandler;

            engine.OnPrompt = CreatePromptHandler();

            engine.OnLocalize = (key, args) =>
                args.Length > 0
                    ? Localization.LocalizationManager.GetString(key, args)
                    : Localization.LocalizationManager.GetString(key);

            UninstallService uninstallService = Services.GetRequiredService<UninstallService>();
            uninstallService.OnLockedFilesDetected = lockedFilesHandler;

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

            TrayIconService trayService = Services.GetRequiredService<TrayIconService>();
            trayService.Show(
                onOpen: () =>
                {
                    if (mainWindow.IsVisible)
                    {
                        if (mainWindow.WindowState == System.Windows.WindowState.Minimized)
                            mainWindow.WindowState = System.Windows.WindowState.Normal;
                        mainWindow.Activate();
                    }
                    else
                    {
                        mainWindow.Show();
                        mainWindow.WindowState = System.Windows.WindowState.Normal;
                        mainWindow.Activate();
                    }
                },
                onExit: () =>
                {
                    trayService.Hide();
                    Current.Shutdown();
                }
            );

            // Listen for duplicate instance signal - show main window when triggered
            _ = Task.Run(() =>
            {
                while (true)
                {
                    _showWindowEvent!.WaitOne();
                    Dispatcher.BeginInvoke(() =>
                    {
                        mainWindow.Show();
                        mainWindow.WindowState = System.Windows.WindowState.Normal;
                        mainWindow.Activate();
                    });
                }
            });

            // Clean up leftover temp directories from previous installs (native DLLs prevent deletion during install)
            _ = Task.Run(() =>
            {
                try
                {
                    string tempDir = StorkDrop.Contracts.Services.StorkPaths.TempDir;
                    if (Directory.Exists(tempDir))
                    {
                        foreach (string dir in Directory.GetDirectories(tempDir))
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });

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

            bool shouldUpdate = false;
            try
            {
                shouldUpdate = Dispatcher.Invoke(() =>
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
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Self-update dialog failed");
                return;
            }

            if (shouldUpdate)
            {
                SelfUpdateService updateService = Services.GetRequiredService<SelfUpdateService>();

                await Dispatcher.InvokeAsync(() =>
                {
                    owner.IsEnabled = false;
                    owner.Title =
                        $"{Branding.Current.WindowTitle} - Downloading update v{update.Version}...";
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
                        owner.Title = Branding.Current.WindowTitle;
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
            Log.Error(ex, "Self-update check failed");
            try
            {
                var logger = Services
                    ?.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?.CreateLogger<App>();
                logger?.LogError(ex, "Self-update check failed");
            }
            catch { }
        }
    }

    /// <summary>
    /// Builds the locked-files dialog handler. Wired both in the interactive UI and in the elevated
    /// child: the child does the actual copy to protected paths, so without this the file-lock dialog
    /// would never appear there and locked files (e.g. STEPS runner processes) would be silently
    /// deferred. The child runs elevated, so ending those processes actually succeeds.
    /// </summary>
    private LockedFilesCallback CreateLockedFilesHandler() =>
        (lockedFiles, detector, directory) =>
        {
            LockedFilesAction result = LockedFilesAction.Skip;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Capture the owner before creating the dialog: the elevated child has no shown main
                    // window, and once the dialog is constructed WPF makes it the app's MainWindow, so
                    // setting Owner = MainWindow would target an unshown window and throw. Only set a
                    // real, already-shown owner (interactive UI); otherwise show it ownerless.
                    Window? owner = MainWindow is { IsLoaded: true } shown ? shown : null;

                    Views.LockedFilesDialog dialog = new Views.LockedFilesDialog(
                        lockedFiles,
                        detector,
                        directory
                    );
                    if (owner is not null && !ReferenceEquals(owner, dialog))
                        dialog.Owner = owner;

                    dialog.ShowDialog();
                    result = dialog.Action;
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Locked files dialog failed");
            }
            return result;
        };

    /// <summary>
    /// Builds the plugin-prompt handler. Wired both in the interactive UI and in the elevated child so
    /// prompts (e.g. "install failed, keep or remove?") can appear during the elevated install. The
    /// owner is captured before the dialog exists: the child has no shown main window, and setting
    /// Owner to an unshown window throws.
    /// </summary>
    private Func<PluginPrompt, PluginPromptResult> CreatePromptHandler() =>
        prompt =>
        {
            PluginPromptResult result = new PluginPromptResult();
            try
            {
                Dispatcher.Invoke(() =>
                {
                    Window? owner = MainWindow is { IsLoaded: true } shown ? shown : null;
                    Views.PluginPromptDialog dialog = new Views.PluginPromptDialog(prompt);
                    if (owner is not null && !ReferenceEquals(owner, dialog))
                        dialog.Owner = owner;
                    if (dialog.ShowDialog() == true)
                        result.ChosenIndex = dialog.ChosenIndex;
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Plugin prompt dialog failed");
            }
            return result;
        };

    private Task RunElevatedInstallAsync(
        string productId,
        string targetPath,
        string feedId,
        string instanceId,
        string version
    )
    {
        Dictionary<string, string>? configValues = LoadElevationConfigFile();

        return RunElevatedAsync(
            "install",
            async services =>
            {
                IFeedRegistry feedRegistry = services.GetRequiredService<IFeedRegistry>();
                IRegistryClient registryClient = feedRegistry.GetClient(feedId);
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();
                engine.OnLockedFilesDetected = CreateLockedFilesHandler();
                engine.OnPrompt = CreatePromptHandler();
                engine.OnLocalize = (key, args) =>
                    args.Length > 0
                        ? Localization.LocalizationManager.GetString(key, args)
                        : Localization.LocalizationManager.GetString(key);

                ProductManifest? manifest = await ResolveElevatedManifestAsync(
                    registryClient,
                    productId,
                    version,
                    feedId
                );
                if (manifest is null)
                    return false;

                InstallOptions options = new InstallOptions(
                    TargetPath: targetPath,
                    InstanceId: instanceId,
                    FeedId: feedId,
                    SkipFileHandlers: true,
                    PluginConfigValues: configValues
                );
                Progress<InstallProgress> progress = new Progress<InstallProgress>(_ => { });
                InstallResult result = await engine.InstallAsync(manifest, options, progress);
                return result.Success;
            }
        );
    }

    private Task RunElevatedUninstallAsync(string productId, string instanceId)
    {
        return RunElevatedAsync(
            "uninstall",
            async services =>
            {
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();
                IProductRepository productRepository =
                    services.GetRequiredService<IProductRepository>();

                InstalledProduct? installed = await productRepository.GetByIdAsync(
                    productId,
                    instanceId
                );
                if (installed is not null)
                    await engine.UninstallAsync(installed);
                return true;
            }
        );
    }

    private Task RunElevatedUpdateAsync(
        string productId,
        string targetPath,
        string feedId,
        string instanceId,
        string version
    )
    {
        Dictionary<string, string>? configValues = LoadElevationConfigFile();

        return RunElevatedAsync(
            "update",
            async services =>
            {
                IFeedRegistry feedRegistry = services.GetRequiredService<IFeedRegistry>();
                IRegistryClient registryClient = feedRegistry.GetClient(feedId);
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();
                engine.OnLockedFilesDetected = CreateLockedFilesHandler();
                engine.OnPrompt = CreatePromptHandler();
                engine.OnLocalize = (key, args) =>
                    args.Length > 0
                        ? Localization.LocalizationManager.GetString(key, args)
                        : Localization.LocalizationManager.GetString(key);
                IProductRepository productRepository =
                    services.GetRequiredService<IProductRepository>();

                InstalledProduct? installed = await productRepository.GetByIdAsync(
                    productId,
                    instanceId
                );
                ProductManifest? manifest = await ResolveElevatedManifestAsync(
                    registryClient,
                    productId,
                    version,
                    feedId
                );

                if (installed is null || manifest is null)
                    return false;

                InstallOptions options = new InstallOptions(
                    TargetPath: targetPath,
                    InstanceId: instanceId,
                    FeedId: feedId,
                    PluginConfigValues: configValues
                );
                Progress<InstallProgress> progress = new Progress<InstallProgress>(_ => { });
                await engine.UpdateAsync(installed, manifest, options, progress);
                return true;
            }
        );
    }

    private Task RunElevatedReExecuteAsync(string productId, string instanceId)
    {
        string[] args = Environment.GetCommandLineArgs();
        bool skipPre = args.Contains("--skip-pre");
        bool skipPost = args.Contains("--skip-post");
        Dictionary<string, string>? configValues = LoadElevationConfigFile();

        return RunElevatedAsync(
            "re-execute",
            async services =>
            {
                IInstallationEngine engine = services.GetRequiredService<IInstallationEngine>();
                engine.OnLockedFilesDetected = CreateLockedFilesHandler();
                engine.OnPrompt = CreatePromptHandler();
                engine.OnLocalize = (key, args) =>
                    args.Length > 0
                        ? Localization.LocalizationManager.GetString(key, args)
                        : Localization.LocalizationManager.GetString(key);
                IProductRepository productRepository =
                    services.GetRequiredService<IProductRepository>();

                InstalledProduct? installed = await productRepository.GetByIdAsync(
                    productId,
                    instanceId
                );
                if (installed is null)
                    return false;

                ReExecuteOptions options = new ReExecuteOptions
                {
                    RunPreInstall = !skipPre,
                    RunPostInstall = !skipPost,
                    // File handlers already ran in the non-elevated parent.
                    RunFileHandlers = false,
                    PluginConfigValues = configValues,
                };
                Progress<InstallProgress> progress = new Progress<InstallProgress>(_ => { });
                InstallResult result = await engine.ReExecutePluginsAsync(
                    installed,
                    options,
                    progress
                );
                return result.Success;
            }
        );
    }

    private async Task RunCliModeAsync(string[] args)
    {
        ConsoleHelper.AttachToParentConsole();
        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();

            CliRunner runner = new CliRunner(Services);
            Environment.ExitCode = await runner.RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CLI error: {ex.Message}");
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

    private async Task RunElevatedAsync(string operation, Func<IServiceProvider, Task<bool>> action)
    {
        try
        {
            _host = AppHostBuilder.Build();
            Services = _host.Services;
            _host.Start();

            IFeedRegistry feedRegistry = Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();

            // Exit code drives the parent's success check - a failed elevated operation must
            // report non-zero so the parent does not register it as installed.
            Environment.ExitCode = await action(Services) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Elevated {Operation} failed", operation);
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

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }

    private static async Task<ProductManifest?> ResolveElevatedManifestAsync(
        IRegistryClient registryClient,
        string productId,
        string version,
        string feedId
    )
    {
        if (string.IsNullOrEmpty(version))
        {
            Log.Error(
                "Elevated operation aborted: no version passed for {ProductId} on feed {FeedId}",
                productId,
                feedId
            );
            return null;
        }

        ProductManifest? manifest = await registryClient.GetProductManifestAsync(
            productId,
            version
        );
        if (manifest is null)
            Log.Error(
                "Elevated operation aborted: version {Version} of {ProductId} not found on feed {FeedId}",
                version,
                productId,
                feedId
            );
        return manifest;
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

            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Swallow on exit
        }

        base.OnExit(e);
    }
}
