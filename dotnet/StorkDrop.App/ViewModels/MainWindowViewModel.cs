using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the main application window, handling navigation and connection status.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly NavigationService _navigationService;
    private readonly IFeedRegistry _feedRegistry;
    private readonly MarketplaceViewModel _marketplaceViewModel;
    private readonly InstalledViewModel _installedViewModel;
    private readonly UpdatesViewModel _updatesViewModel;
    private readonly ActivityLogViewModel _activityLogViewModel;
    private readonly PluginsViewModel _pluginsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IEnumerable<IStorkDropPlugin> _plugins;
    private readonly PluginLoadStatus _pluginLoadStatus;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(
        NavigationService navigationService,
        IFeedRegistry feedRegistry,
        MarketplaceViewModel marketplaceViewModel,
        InstalledViewModel installedViewModel,
        UpdatesViewModel updatesViewModel,
        ActivityLogViewModel activityLogViewModel,
        PluginsViewModel pluginsViewModel,
        SettingsViewModel settingsViewModel,
        ILogger<MainWindowViewModel> logger,
        IEnumerable<IStorkDropPlugin> plugins,
        PluginLoadStatus pluginLoadStatus
    )
    {
        _navigationService = navigationService;
        _feedRegistry = feedRegistry;
        _marketplaceViewModel = marketplaceViewModel;
        _installedViewModel = installedViewModel;
        _updatesViewModel = updatesViewModel;
        _activityLogViewModel = activityLogViewModel;
        _pluginsViewModel = pluginsViewModel;
        _settingsViewModel = settingsViewModel;
        _logger = logger;
        _plugins = plugins;
        _pluginLoadStatus = pluginLoadStatus;

        _marketplaceViewModel.NavigateToProductDetail += OnNavigateToProductDetail;

        BuildPluginNavTabs();
        BuildPluginStatusText();
    }

    [ObservableProperty]
    private string _selectedNavItem = "Marketplace";

    [ObservableProperty]
    private object? _currentContent;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _pluginStatusText = string.Empty;

    [ObservableProperty]
    private System.Windows.Media.SolidColorBrush _pluginStatusColor =
        new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32")
        );

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<PluginNavTabViewModel> _pluginNavTabs =
        new System.Collections.ObjectModel.ObservableCollection<PluginNavTabViewModel>();

    private void BuildPluginNavTabs()
    {
        foreach (IStorkDropPlugin plugin in _plugins)
        {
            try
            {
                System.Collections.Generic.IReadOnlyList<PluginNavTab> tabs =
                    plugin.GetNavigationTabs();
                foreach (PluginNavTab tab in tabs)
                {
                    PluginNavTabs.Add(
                        new PluginNavTabViewModel
                        {
                            TabId = tab.TabId,
                            DisplayName = tab.DisplayName,
                            Icon = tab.Icon,
                            PluginId = plugin.PluginId,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to get navigation tabs from plugin {PluginId}",
                    plugin.PluginId
                );
            }
        }
    }

    private void BuildPluginStatusText()
    {
        int total = _pluginLoadStatus.TotalPluginDlls;
        int loaded = _pluginLoadStatus.LoadedCount;

        if (total == 0)
        {
            PluginStatusText = string.Empty;
            return;
        }

        PluginStatusText = LocalizationManager
            .GetString("PluginStatus_AllLoaded")
            .Replace("{0}", loaded.ToString())
            .Replace("{1}", total.ToString());

        if (_pluginLoadStatus.FailedCount > 0)
        {
            PluginStatusColor = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString("#FF8F00")
            );
        }
        else
        {
            PluginStatusColor = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32")
            );
        }
    }

    /// <summary>
    /// Initializes the main window by registering the content region and navigating to the default view.
    /// </summary>
    /// <param name="contentRegion">The content control to use for navigation.</param>
    public void Initialize(ContentControl contentRegion)
    {
        _navigationService.RegisterContentRegion(contentRegion);
        StatusMessage = LocalizationManager.GetString("Status_Connecting");
        NavigateToCommand.Execute("Marketplace");
        _ = CheckConnectionSafeAsync();
    }

    /// <summary>
    /// Navigates to the specified view.
    /// </summary>
    /// <param name="viewName">The name of the view to navigate to.</param>
    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        SelectedNavItem = viewName;

        object viewModel = viewName switch
        {
            "Marketplace" => _marketplaceViewModel,
            "Installed" => _installedViewModel,
            "Updates" => _updatesViewModel,
            "ActivityLog" => _activityLogViewModel,
            "Plugins" => _pluginsViewModel,
            "Settings" => _settingsViewModel,
            _ => _marketplaceViewModel,
        };

        CurrentContent = viewModel;
    }

    /// <summary>
    /// Navigates to a plugin tab by its tab ID.
    /// </summary>
    /// <param name="tab">The plugin nav tab to navigate to.</param>
    [RelayCommand]
    private void NavigateToPluginTab(PluginNavTabViewModel tab)
    {
        foreach (IStorkDropPlugin plugin in _plugins)
        {
            if (plugin.PluginId == tab.PluginId)
            {
                try
                {
                    plugin.OnNavigationTabSelected(tab.TabId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Plugin {PluginId} failed to handle tab selection for {TabId}",
                        plugin.PluginId,
                        tab.TabId
                    );
                }

                break;
            }
        }
    }

    /// <summary>
    /// Navigates to the Plugins tab (used from status bar plugin status click).
    /// </summary>
    [RelayCommand]
    private void NavigateToPlugins()
    {
        NavigateTo("Plugins");
    }

    private void OnNavigateToProductDetail(string productId, string feedId)
    {
        ProductDetailViewModel detailVm = App.Services.GetRequiredService<ProductDetailViewModel>();
        detailVm.FeedId = feedId;
        detailVm.GoBackRequested += () => NavigateTo("Marketplace");
        detailVm.LoadCommand.Execute(productId);
        CurrentContent = detailVm;
    }

    private async Task CheckConnectionSafeAsync()
    {
        try
        {
            await CheckConnectionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection check failed");
            IsConnected = false;
            StatusMessage = LocalizationManager.GetString("Status_Disconnected");
            IsLoading = false;
        }
    }

    private async Task CheckConnectionAsync()
    {
        try
        {
            IsLoading = true;
            using CancellationTokenSource cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(10)
            );

            IReadOnlyList<FeedInfo> feeds = _feedRegistry.GetFeeds();
            if (feeds.Count == 0)
            {
                IsConnected = false;
                StatusMessage = LocalizationManager.GetString("Status_Disconnected");
                return;
            }

            bool anyConnected = false;
            foreach (FeedInfo feed in feeds)
            {
                try
                {
                    if (await _feedRegistry.TestConnectionAsync(feed.Id, cts.Token))
                    {
                        anyConnected = true;
                        break;
                    }
                }
                catch { }
            }

            IsConnected = anyConnected;
            StatusMessage = IsConnected
                ? LocalizationManager.GetString("Status_Connected")
                : LocalizationManager.GetString("Status_Disconnected");
        }
        catch (OperationCanceledException)
        {
            IsConnected = false;
            StatusMessage = LocalizationManager.GetString("Status_Timeout");
        }
        catch
        {
            IsConnected = false;
            StatusMessage = LocalizationManager.GetString("Status_Disconnected");
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>
/// Represents a plugin-contributed navigation tab in the sidebar.
/// </summary>
public partial class PluginNavTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _tabId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;
}
