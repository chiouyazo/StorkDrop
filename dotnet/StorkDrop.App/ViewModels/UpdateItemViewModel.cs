using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single available update in the updates list.
/// </summary>
public partial class UpdateItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _currentVersion = string.Empty;

    [ObservableProperty]
    private string _availableVersion = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;
}
