using CommunityToolkit.Mvvm.ComponentModel;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model representing a single feed configuration in the settings UI.
/// </summary>
public partial class FeedViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _repository = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private string _connectionTestMessage = string.Empty;

    [ObservableProperty]
    private bool _isConnectionValid;

    /// <summary>
    /// Whether this feed requires a soft-lock password before install/update/uninstall/action.
    /// </summary>
    [ObservableProperty]
    private bool _requireLockPassword;

    /// <summary>
    /// True for a white-label edition's pre-configured feed: name and URL are vendor-fixed and the
    /// feed cannot be removed.
    /// </summary>
    [ObservableProperty]
    private bool _isIdentityLocked;

    /// <summary>
    /// True when the lock password is imposed by the white-label config, so the user cannot toggle
    /// or change it.
    /// </summary>
    [ObservableProperty]
    private bool _isLockManaged;

    public bool CanRemove => !IsIdentityLocked;

    public bool CanEditLock => !IsLockManaged;

    public bool CanEnterLockPassword => RequireLockPassword && !IsLockManaged;

    partial void OnIsIdentityLockedChanged(bool value) => OnPropertyChanged(nameof(CanRemove));

    partial void OnIsLockManagedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditLock));
        OnPropertyChanged(nameof(CanEnterLockPassword));
    }

    partial void OnRequireLockPasswordChanged(bool value) =>
        OnPropertyChanged(nameof(CanEnterLockPassword));

    /// <summary>
    /// A newly entered lock password (transient). When empty while <see cref="RequireLockPassword"/>
    /// is set, the existing password (<see cref="ExistingLockHash"/>) is kept unchanged.
    /// </summary>
    [ObservableProperty]
    private string _lockPassword = string.Empty;

    /// <summary>
    /// The lock password hash loaded from configuration. Never shown; preserved on save unless
    /// the user disables the lock or types a new password.
    /// </summary>
    public string? ExistingLockHash { get; set; }

    /// <summary>
    /// Endpoint that receives inventory status reports for this feed's products. Empty = no reporting.
    /// </summary>
    [ObservableProperty]
    private string _reportUrl = string.Empty;

    /// <summary>Shared secret used to HMAC-sign this feed's reports (stored encrypted).</summary>
    [ObservableProperty]
    private string _reportSecret = string.Empty;

    /// <summary>Optional human-friendly deployment/customer label included in this feed's reports.</summary>
    [ObservableProperty]
    private string _reportCustomerId = string.Empty;
}
