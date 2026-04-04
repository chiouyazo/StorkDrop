using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StorkDrop.App.Converters;
using StorkDrop.App.Localization;
using StorkDrop.App.ViewModels;
using StorkDrop.App.Views;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo;

public class DemoApp : Application
{
    [STAThread]
    public static void Main()
    {
        DemoApp app = new DemoApp();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.LoadResources();
        app.Run();
    }

    private IHost? _host;

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

        try
        {
            _host = DemoHostBuilder.Build();
            StorkDrop.App.App.Services = _host.Services;
            _host.Start();

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

            MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Title = "StorkDrop (Demo)";
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Demo startup failed: {ex}", "Error");
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
                _host.Dispose();
            }
        }
        catch { }
        base.OnExit(e);
    }
}
