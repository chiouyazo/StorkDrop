using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Contracts;

namespace StorkDrop.App.ViewModels;

public partial class PluginsViewModel : ObservableObject
{
    private readonly IEnumerable<IStorkDropPlugin> _plugins;
    private readonly PluginLoadStatus _loadStatus;

    [ObservableProperty]
    private ObservableCollection<PluginInfoViewModel> _loadedPlugins = [];

    public PluginsViewModel(IEnumerable<IStorkDropPlugin> plugins, PluginLoadStatus loadStatus)
    {
        _plugins = plugins;
        _loadStatus = loadStatus;
        LoadPlugins();
    }

    private void LoadPlugins()
    {
        List<PluginInfoViewModel> items = [];

        // Successfully loaded plugins
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

        // Failed plugins
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
