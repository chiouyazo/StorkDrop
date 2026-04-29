using CommunityToolkit.Mvvm.ComponentModel;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single installed product in the installed products list.
/// </summary>
public partial class InstalledProductViewModel : ObservableObject
{
    [ObservableProperty]
    private string _productId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _installedPath = string.Empty;

    [ObservableProperty]
    private DateTime _installedDate;

    [ObservableProperty]
    private bool _hasPlugins;

    [ObservableProperty]
    private bool _hasFileHandlerData;

    [ObservableProperty]
    private InstallType _installType;

    [ObservableProperty]
    private string? _feedId;

    [ObservableProperty]
    private string _instanceId = string.Empty;

    [ObservableProperty]
    private string? _badgeText;

    [ObservableProperty]
    private string? _badgeColor;

    public bool IsExecutable => InstallType == InstallType.Executable;
    public bool HasActions => HasPlugins || HasFileHandlerData;
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);
    public bool HasFeed => !string.IsNullOrEmpty(FeedId);
    public bool IsDefaultInstance => InstanceId == InstanceIdHelper.DefaultInstanceId;
    public string DisplayName => IsDefaultInstance ? Title : $"{Title} ({InstanceId})";

    partial void OnBadgeTextChanged(string? value) => OnPropertyChanged(nameof(HasBadge));

    partial void OnFeedIdChanged(string? value) => OnPropertyChanged(nameof(HasFeed));

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDefaultInstance));
    }

    partial void OnInstanceIdChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDefaultInstance));
    }
}
