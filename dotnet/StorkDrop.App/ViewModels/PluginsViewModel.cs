using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.App.ViewModels;

public partial class PluginsViewModel : ObservableObject
{
    private readonly IEnumerable<IStorkDropPlugin> _plugins;
    private readonly PluginLoadStatus _loadStatus;
    private readonly ILogger<PluginsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<PluginInfoViewModel> _loadedPlugins = [];

    public PluginsViewModel(
        IEnumerable<IStorkDropPlugin> plugins,
        PluginLoadStatus loadStatus,
        ILogger<PluginsViewModel> logger
    )
    {
        _plugins = plugins;
        _loadStatus = loadStatus;
        _logger = logger;
        LoadPlugins();
    }

    private void LoadPlugins()
    {
        List<PluginInfoViewModel> items = [];

        foreach (IStorkDropPlugin p in _plugins)
        {
            items.Add(
                new PluginInfoViewModel
                {
                    DisplayName = p.DisplayName,
                    PluginId = p.PluginId,
                    AssociatedFeeds = string.Join(", ", p.AssociatedFeeds),
                }
            );
        }

        foreach (PluginLoadError error in _loadStatus.Errors)
        {
            items.Add(
                new PluginInfoViewModel
                {
                    DisplayName = error.DllPath,
                    PluginId = "Failed to load",
                    IsFailed = true,
                    ErrorMessage = error.ErrorMessage,
                }
            );
        }

        LoadedPlugins = new ObservableCollection<PluginInfoViewModel>(items);
    }
}
