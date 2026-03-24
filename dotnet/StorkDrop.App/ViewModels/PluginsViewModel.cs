using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Contracts;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the plugins view, displaying loaded StorkDrop plugins.
/// </summary>
public partial class PluginsViewModel : ObservableObject
{
    private readonly IEnumerable<IStorkDropPlugin> _plugins;

    [ObservableProperty]
    private ObservableCollection<PluginInfoViewModel> _loadedPlugins =
        new ObservableCollection<PluginInfoViewModel>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginsViewModel"/> class.
    /// </summary>
    /// <param name="plugins">The collection of loaded StorkDrop plugins.</param>
    public PluginsViewModel(IEnumerable<IStorkDropPlugin> plugins)
    {
        _plugins = plugins;
        LoadPlugins();
    }

    private void LoadPlugins()
    {
        LoadedPlugins = new ObservableCollection<PluginInfoViewModel>(
            _plugins.Select(p => new PluginInfoViewModel
            {
                DisplayName = p.DisplayName,
                PluginId = p.PluginId,
                AssociatedFeeds = string.Join(", ", p.AssociatedFeeds),
            })
        );
    }
}
