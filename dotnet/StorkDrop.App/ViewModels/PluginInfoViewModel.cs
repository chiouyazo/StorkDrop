using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

public partial class PluginInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private string _associatedFeeds = string.Empty;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;
}
