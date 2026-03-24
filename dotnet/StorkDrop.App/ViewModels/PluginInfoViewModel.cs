using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single loaded plugin in the plugins list.
/// </summary>
public partial class PluginInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private string _associatedFeeds = string.Empty;
}
