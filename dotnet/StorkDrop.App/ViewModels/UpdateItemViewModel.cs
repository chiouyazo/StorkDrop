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

    [ObservableProperty]
    private string _installedPath = string.Empty;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private int _updatePercentage;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _showReleaseNotes;

    [ObservableProperty]
    private string _feedId = string.Empty;

    [ObservableProperty]
    private string _instanceId = string.Empty;

    [ObservableProperty]
    private string _feedName = string.Empty;

    [ObservableProperty]
    private string? _badgeText;

    [ObservableProperty]
    private string? _badgeColor;

    [ObservableProperty]
    private string? _currentBadgeText;

    public bool HasFeedName => !string.IsNullOrEmpty(FeedName);

    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    public bool HasBadgeChange =>
        !string.Equals(
            BadgeText ?? string.Empty,
            CurrentBadgeText ?? string.Empty,
            StringComparison.OrdinalIgnoreCase
        );

    public string CurrentBadgeDisplay => DisplayOrDash(CurrentBadgeText);

    public string BadgeDisplay => DisplayOrDash(BadgeText);

    partial void OnFeedNameChanged(string value) => OnPropertyChanged(nameof(HasFeedName));

    partial void OnBadgeTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(HasBadgeChange));
        OnPropertyChanged(nameof(BadgeDisplay));
    }

    partial void OnCurrentBadgeTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasBadgeChange));
        OnPropertyChanged(nameof(CurrentBadgeDisplay));
    }

    private static string DisplayOrDash(string? badge) => string.IsNullOrEmpty(badge) ? "-" : badge;
}
