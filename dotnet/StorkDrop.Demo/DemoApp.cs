using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Converters;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Sandbox;
using StorkDrop.Demo.Services;
using StorkDrop.Demo.Ui;
using StorkDrop.Installer;
using StorkDrop.Registry;

namespace StorkDrop.Demo;

public class DemoApp : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (DemoLockHolder.TryRunAsHolder(args))
            return;

        DemoSandbox.Initialize();
        DemoApp app = new DemoApp();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.LoadResources();
        app.Run();
    }

    private IHost? _host;
    private FeedRegistryConfigWatcher? _feedConfigWatcher;
    private Process? _lockHolder;

    internal void LoadResources()
    {
        Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/StorkDrop.App;component/Themes/Colors.xaml"
                ),
            }
        );
        Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/StorkDrop.App;component/Themes/Typography.xaml"
                ),
            }
        );
        Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/StorkDrop.App;component/Themes/Controls.xaml"
                ),
            }
        );
        Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/StorkDrop.App;component/Localization/Strings.en.xaml"
                ),
            }
        );
        Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        Resources.Add("InstallTypeToIconConverter", new InstallTypeToIconConverter());
        Resources.Add("InverseBoolConverter", new InverseBoolConverter());
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Serilog.Log.Error(args.Exception, "Unhandled exception on UI thread");
            MessageBox.Show(args.Exception.ToString(), "Demo: unhandled exception");
            args.Handled = true;
        };

        try
        {
            _host = DemoHostBuilder.Build();
            StorkDrop.App.App.Services = _host.Services;
            _host.Start();

            DemoFeedRegistry demoRegistry = _host.Services.GetRequiredService<DemoFeedRegistry>();
            await DemoSeeder.SeedAsync(
                demoRegistry,
                _host.Services.GetRequiredService<IProductRepository>(),
                _host.Services.GetRequiredService<DemoLockState>()
            );
            _lockHolder = DemoLockHolder.Start();
            _feedConfigWatcher = new FeedRegistryConfigWatcher(
                StorkPaths.ConfigDir,
                "config.json",
                demoRegistry,
                _host
                    .Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<FeedRegistryConfigWatcher>()
            );

            LocalizationManager.Initialize("en");

            IFeedRegistry feedRegistry = _host.Services.GetRequiredService<IFeedRegistry>();
            await feedRegistry.ReloadAsync();

            IInstallationEngine engine = _host.Services.GetRequiredService<IInstallationEngine>();

            engine.OnFileHandlerConfigNeeded = (fields, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                Dispatcher.Invoke(() =>
                {
                    PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                        fields,
                        currentValues
                    );
                    PluginConfigDialog dialog = new PluginConfigDialog { DataContext = vm };
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
                    PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                        fields,
                        currentValues
                    );
                    vm.InteractivePlugin = engine.CurrentInteractivePlugin;
                    PluginConfigDialog dialog = new PluginConfigDialog { DataContext = vm };
                    dialog.Owner = MainWindow;
                    if (dialog.ShowDialog() == true)
                        result = vm.GetValues();
                });
                return result;
            };

            engine.OnActionGroupConfigNeeded = (groups, currentValues) =>
            {
                Dictionary<string, string>? result = null;
                Dispatcher.Invoke(() =>
                {
                    PluginConfigDialogViewModel vm = new PluginConfigDialogViewModel(
                        groups,
                        currentValues
                    );
                    vm.InteractivePlugin = engine.CurrentInteractivePlugin;
                    PluginConfigDialog dialog = new PluginConfigDialog { DataContext = vm };
                    dialog.Owner = MainWindow;
                    if (dialog.ShowDialog() == true)
                        result = vm.GetValues();
                });
                return result;
            };

            IEnumerable<IStorkDropPlugin> allPlugins =
                _host.Services.GetServices<IStorkDropPlugin>();
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

            engine.OnLockedFilesDetected = CreateLockedFilesHandler();
            _host.Services.GetRequiredService<UninstallService>().OnLockedFilesDetected =
                engine.OnLockedFilesDetected;
            engine.OnPrompt = CreatePromptHandler();
            engine.OnPickInstalledInstance = CreateInstancePickerHandler();
            engine.OnLocalize = (key, args) =>
                args.Length > 0
                    ? LocalizationManager.GetString(key, args)
                    : LocalizationManager.GetString(key);

            MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Title = "StorkDrop (Demo)";
            mainWindow.Show();

            DemoControlWindow control = new DemoControlWindow(
                demoRegistry,
                _host.Services.GetRequiredService<DemoControlState>(),
                _host.Services.GetRequiredService<DemoLockState>(),
                _lockHolder
            );
            control.Owner = mainWindow;
            control.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Demo startup failed: {ex}", "Error");
            Shutdown();
        }
    }

    private LockedFilesCallback CreateLockedFilesHandler() =>
        (lockedFiles, detector, directory) =>
        {
            LockedFilesAction result = LockedFilesAction.Skip;
            Dispatcher.Invoke(() =>
            {
                LockedFilesDialog dialog = new LockedFilesDialog(lockedFiles, detector, directory);
                if (MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                dialog.ShowDialog();
                result = dialog.Action;
            });
            return result;
        };

    private Func<PluginPrompt, PluginPromptResult> CreatePromptHandler() =>
        prompt =>
        {
            PluginPromptResult result = new PluginPromptResult();
            Dispatcher.Invoke(() =>
            {
                PluginPromptDialog dialog = new PluginPromptDialog(prompt);
                if (MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                if (dialog.ShowDialog() == true)
                {
                    result.ChosenIndex = dialog.ChosenIndex;
                    result.FieldValues = dialog.FieldValues;
                }
            });
            return result;
        };

    private Func<InstancePickRequest, InstalledProduct?> CreateInstancePickerHandler() =>
        request =>
        {
            InstalledProduct? result = null;
            IReadOnlyList<InstalledProduct> instances = _host!
                .Services.GetRequiredService<IInstalledProductsSnapshot>()
                .GetInstances(request.ProductId);
            if (instances.Count == 0)
                return null;

            Dispatcher.Invoke(() =>
            {
                SelectInstanceDialog dialog = new SelectInstanceDialog(
                    request.ProductId,
                    request.ProductId,
                    instances,
                    request.Message
                );
                if (MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, dialog))
                    dialog.Owner = owner;
                if (dialog.ShowDialog() == true)
                    result = dialog.SelectedInstance;
            });
            return result;
        };

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_lockHolder is { HasExited: false })
                _lockHolder.Kill();

            _feedConfigWatcher?.Dispose();
            _host?.Dispose();
        }
        catch { }
        DemoSandbox.Cleanup();
        base.OnExit(e);
    }
}
