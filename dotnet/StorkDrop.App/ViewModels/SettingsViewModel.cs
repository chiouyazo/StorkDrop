using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Registry;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the settings view, managing feeds, proxy, language, and application preferences.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly IFeedConnectionService _connectionService;
    private readonly ISelfUpdateChecker _selfUpdateChecker;
    private readonly Services.SelfUpdateService _selfUpdateService;
    private readonly DialogService _dialogService;
    private readonly IEnumerable<IStorkDropPlugin> _plugins;
    private readonly IFeedRegistry _feedRegistry;
    private readonly IFeedLockService _feedLock;
    private readonly IFeedReportService _feedReport;
    private readonly ILogger<SettingsViewModel> _logger;

    private readonly Dictionary<string, FeedFields> _originalFeedFields = new Dictionary<
        string,
        FeedFields
    >(StringComparer.Ordinal);

    public SettingsViewModel(
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IFeedConnectionService connectionService,
        ISelfUpdateChecker selfUpdateChecker,
        Services.SelfUpdateService selfUpdateService,
        DialogService dialogService,
        IEnumerable<IStorkDropPlugin> plugins,
        IFeedRegistry feedRegistry,
        IFeedLockService feedLock,
        IFeedReportService feedReport,
        ILogger<SettingsViewModel> logger
    )
    {
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _connectionService = connectionService;
        _selfUpdateChecker = selfUpdateChecker;
        _selfUpdateService = selfUpdateService;
        _dialogService = dialogService;
        _plugins = plugins;
        _feedRegistry = feedRegistry;
        _feedLock = feedLock;
        _feedReport = feedReport;
        _logger = logger;

        BuildRecommendedFeeds();
    }

    [ObservableProperty]
    private ObservableCollection<FeedViewModel> _feeds = new ObservableCollection<FeedViewModel>();

    /// <summary>False when a white-label edition forbids adding feeds through the UI.</summary>
    public bool CanAddFeeds => !Branding.Current.ForbidNewFeeds;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _autoCheckForUpdates;

    [ObservableProperty]
    private int _checkIntervalHours = 4;

    [ObservableProperty]
    private string _proxyHost = string.Empty;

    [ObservableProperty]
    private int _proxyPort;

    [ObservableProperty]
    private string _selectedLanguage = "en";

    [ObservableProperty]
    private string _selectedLogLevel = "Information";

    [ObservableProperty]
    private bool _checkForStorkDropUpdates = true;

    [ObservableProperty]
    private bool _includeDevVersions;

    [ObservableProperty]
    private bool _runInBackground = true;

    /// <summary>
    /// Comma-separated channels shown from channel-aware feeds (S3). Empty defaults to "prod".
    /// Customer editions keep this at prod; operators add "dev, feature".
    /// </summary>
    [ObservableProperty]
    private string _visibleChannels = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string _updateCheckResult = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _connectionTestMessage = string.Empty;

    [ObservableProperty]
    private bool _isConnectionValid;

    [ObservableProperty]
    private ObservableCollection<RecommendedFeedViewModel> _recommendedFeeds =
        new ObservableCollection<RecommendedFeedViewModel>();

    [ObservableProperty]
    private bool _showRecommendedFeeds;

    /// <summary>
    /// Gets the list of available UI languages.
    /// </summary>
    public IReadOnlyList<string> AvailableLanguages => LocalizationManager.AvailableLanguages;

    /// <summary>
    /// Gets the list of available log levels.
    /// </summary>
    public IReadOnlyList<string> AvailableLogLevels =>
        new List<string> { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };

    /// <summary>
    /// Gets whether there are recommended feeds available from plugins.
    /// </summary>
    public bool HasRecommendedFeeds => RecommendedFeeds.Count > 0;

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationManager.Language = value;
    }

    private void BuildRecommendedFeeds()
    {
        List<RecommendedFeedViewModel> recommended = new List<RecommendedFeedViewModel>();
        foreach (IStorkDropPlugin plugin in _plugins)
        {
            if (plugin.AssociatedFeeds is not null)
            {
                foreach (string feedUrl in plugin.AssociatedFeeds)
                {
                    if (!string.IsNullOrEmpty(feedUrl))
                    {
                        recommended.Add(
                            new RecommendedFeedViewModel
                            {
                                Url = feedUrl,
                                PluginName = plugin.DisplayName,
                                PluginId = plugin.PluginId,
                            }
                        );
                    }
                }
            }
        }

        RecommendedFeeds = new ObservableCollection<RecommendedFeedViewModel>(recommended);
    }

    /// <summary>
    /// Loads the current configuration and populates the view model.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            AppConfiguration? config = await Task.Run(() => _configurationService.LoadAsync());
            if (config is null)
                return;

            AutoStart = config.AutoStart;
            AutoCheckForUpdates = config.AutoCheckForUpdates;
            CheckIntervalHours = (int)config.CheckInterval.TotalHours;

            CheckForStorkDropUpdates = config.CheckForStorkDropUpdates;
            IncludeDevVersions = config.IncludeDevVersions;
            RunInBackground = config.RunInBackground;
            string[]? effectiveChannels =
                Branding.Current.VisibleChannels ?? config.VisibleChannels;
            VisibleChannels = effectiveChannels is { Length: > 0 } vc
                ? string.Join(", ", vc)
                : string.Empty;

            SelectedLanguage = config.Language;
            SelectedLogLevel = config.LogLevel ?? "Information";

            if (config.ProxySettings is not null)
            {
                ProxyHost = config.ProxySettings.Host;
                ProxyPort = config.ProxySettings.Port;
            }

            Feeds = new ObservableCollection<FeedViewModel>(
                config.Feeds.Select(f =>
                {
                    string decryptedPassword = string.Empty;
                    if (!string.IsNullOrEmpty(f.EncryptedPassword))
                    {
                        try
                        {
                            decryptedPassword = _encryptionService.Decrypt(f.EncryptedPassword);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to decrypt password for feed {FeedId}",
                                f.Id
                            );
                            decryptedPassword = string.Empty;
                        }
                    }

                    string decryptedReportSecret = string.Empty;
                    if (!string.IsNullOrEmpty(f.EncryptedReportSecret))
                    {
                        try
                        {
                            decryptedReportSecret = _encryptionService.Decrypt(
                                f.EncryptedReportSecret
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to decrypt report secret for feed {FeedId}",
                                f.Id
                            );
                            decryptedReportSecret = string.Empty;
                        }
                    }

                    string decryptedS3Secret = string.Empty;
                    if (!string.IsNullOrEmpty(f.S3?.EncryptedSecretKey))
                    {
                        try
                        {
                            decryptedS3Secret = _encryptionService.Decrypt(f.S3.EncryptedSecretKey);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to decrypt S3 secret for feed {FeedId}",
                                f.Id
                            );
                            decryptedS3Secret = string.Empty;
                        }
                    }

                    bool isWhitelabel =
                        Branding.Current.HasFeed && f.Id == Branding.WhitelabelFeedId;
                    BrandingFeed? brandFeed = Branding.Current.Feed;
                    bool hasManagedLock =
                        isWhitelabel && !string.IsNullOrEmpty(brandFeed?.LockPasswordHash);

                    bool brandedS3 =
                        isWhitelabel
                        && brandFeed?.Provider == FeedProvider.S3
                        && brandFeed.S3 is not null;
                    BrandingS3? brandS3 = brandedS3 ? brandFeed!.S3 : null;
                    FeedProvider provider = brandedS3 ? FeedProvider.S3 : f.Provider;
                    string[]? s3Channels = brandS3?.Channels ?? f.S3?.Channels;

                    return new FeedViewModel
                    {
                        Id = f.Id,
                        Provider = provider,
                        S3Bucket = brandS3?.Bucket ?? f.S3?.Bucket ?? string.Empty,
                        S3Region = brandS3?.Region ?? f.S3?.Region ?? string.Empty,
                        S3ServiceUrl = brandS3?.ServiceUrl ?? f.S3?.ServiceUrl ?? string.Empty,
                        S3UsePathStyle = brandS3?.UsePathStyle ?? f.S3?.UsePathStyle ?? false,
                        S3AccessKeyId = f.S3?.AccessKeyId ?? string.Empty,
                        S3SecretKey = decryptedS3Secret,
                        S3Prefix = brandS3?.Prefix ?? f.S3?.Prefix ?? string.Empty,
                        S3Channels = s3Channels is { Length: > 0 } channels
                            ? string.Join(", ", channels)
                            : string.Empty,
                        ExistingS3EncryptedSecretKey = f.S3?.EncryptedSecretKey,
                        Name =
                            isWhitelabel && !string.IsNullOrWhiteSpace(brandFeed?.Name)
                                ? brandFeed!.Name!
                                : f.Name,
                        Url =
                            isWhitelabel && !string.IsNullOrWhiteSpace(brandFeed?.Url)
                                ? brandFeed!.Url!
                                : f.Url,
                        Repository = f.Repository ?? string.Empty,
                        Username = f.Username ?? string.Empty,
                        Password = decryptedPassword,
                        PluginId = f.PluginId ?? string.Empty,
                        RequireLockPassword =
                            hasManagedLock || !string.IsNullOrEmpty(f.LockPasswordHash),
                        ExistingLockHash = hasManagedLock
                            ? brandFeed!.LockPasswordHash
                            : f.LockPasswordHash,
                        IsIdentityLocked = isWhitelabel,
                        IsLockManaged = hasManagedLock,
                        ReportUrl = f.ReportUrl ?? string.Empty,
                        ReportSecret = decryptedReportSecret,
                        ReportCustomerId = f.ReportCustomerId ?? string.Empty,
                    };
                })
            );

            _originalFeedFields.Clear();
            foreach (FeedViewModel feed in Feeds)
                _originalFeedFields[feed.Id] = Capture(feed);
        }
        catch (Exception ex)
        {
            ErrorMessage =
                LocalizationManager.GetString("Error_LoadSettingsFailed") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// Adds a new empty feed configuration. Shows recommended feeds if available.
    /// </summary>
    [RelayCommand]
    private void AddFeed()
    {
        if (!CanAddFeeds)
            return;

        ShowRecommendedFeeds = HasRecommendedFeeds;
        Feeds.Add(
            new FeedViewModel { Id = Guid.NewGuid().ToString(), Name = $"Feed {Feeds.Count + 1}" }
        );
    }

    /// <summary>
    /// Adds a recommended feed from a plugin suggestion.
    /// </summary>
    /// <param name="recommended">The recommended feed to add.</param>
    [RelayCommand]
    private void AddRecommendedFeed(RecommendedFeedViewModel recommended)
    {
        Feeds.Add(
            new FeedViewModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = recommended.PluginName + " Feed",
                Url = recommended.Url,
                PluginId = recommended.PluginId,
            }
        );
        ShowRecommendedFeeds = false;
    }

    /// <summary>
    /// Removes the specified feed configuration.
    /// </summary>
    /// <param name="feed">The feed to remove.</param>
    [RelayCommand]
    private void RemoveFeed(FeedViewModel feed)
    {
        if (feed.IsIdentityLocked)
            return;

        Feeds.Remove(feed);
    }

    /// <summary>
    /// Saves the current settings to disk.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            ErrorMessage = string.Empty;

            if (!await AuthorizeLockedFeedChangesAsync())
            {
                ErrorMessage = LocalizationManager.GetString("FeedLock_SaveCancelled");
                return;
            }

            List<string> feedsNeedingInitialReport = FeedsWithChangedReportConfig();

            FeedConfiguration[] feeds = Feeds
                .Select(f =>
                {
                    bool isWhitelabel =
                        Branding.Current.HasFeed && f.Id == Branding.WhitelabelFeedId;
                    BrandingFeed? brandFeed = Branding.Current.Feed;

                    string name =
                        isWhitelabel && !string.IsNullOrWhiteSpace(brandFeed?.Name)
                            ? brandFeed!.Name!
                            : f.Name;
                    string url =
                        isWhitelabel && !string.IsNullOrWhiteSpace(brandFeed?.Url)
                            ? brandFeed!.Url!
                            : f.Url;
                    string? lockHash =
                        isWhitelabel && !string.IsNullOrEmpty(brandFeed?.LockPasswordHash)
                            ? brandFeed!.LockPasswordHash
                            : ResolveLockHash(f);

                    return new FeedConfiguration(
                        f.Id,
                        name,
                        url,
                        !string.IsNullOrWhiteSpace(f.Repository) ? f.Repository : null,
                        !string.IsNullOrEmpty(f.Username) ? f.Username : null,
                        !string.IsNullOrEmpty(f.Password)
                            ? _encryptionService.Encrypt(f.Password)
                            : null,
                        !string.IsNullOrEmpty(f.PluginId) ? f.PluginId : null,
                        lockHash,
                        !string.IsNullOrWhiteSpace(f.ReportUrl) ? f.ReportUrl.Trim() : null,
                        !string.IsNullOrEmpty(f.ReportSecret)
                            ? _encryptionService.Encrypt(f.ReportSecret)
                            : null,
                        !string.IsNullOrWhiteSpace(f.ReportCustomerId)
                            ? f.ReportCustomerId.Trim()
                            : null,
                        f.Provider,
                        BuildS3Settings(f)
                    );
                })
                .ToArray();

            ProxySettings? proxy = !string.IsNullOrEmpty(ProxyHost)
                ? new ProxySettings(ProxyHost, ProxyPort)
                : null;

            LocalizationManager.Language = SelectedLanguage;

            AppConfiguration config = new AppConfiguration(
                Feeds: feeds,
                AutoStart: AutoStart,
                AutoCheckForUpdates: AutoCheckForUpdates,
                CheckInterval: TimeSpan.FromHours(CheckIntervalHours),
                ProxySettings: proxy,
                Language: SelectedLanguage,
                LogLevel: SelectedLogLevel,
                CheckForStorkDropUpdates: CheckForStorkDropUpdates,
                IncludeDevVersions: IncludeDevVersions,
                RunInBackground: RunInBackground,
                VisibleChannels: Branding.Current.VisibleChannels ?? ParseChannels(VisibleChannels)
            );

            await Task.Run(() => _configurationService.SaveAsync(config));

            await Task.Run(() => _feedRegistry.ReloadAsync());

            SyncFeedStateAfterSave(feeds);

            ErrorMessage = string.Empty;

            foreach (string feedId in feedsNeedingInitialReport)
            {
                try
                {
                    await _feedReport.NotifyFeedChangedAsync(feedId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Initial feed report failed for {FeedId}", feedId);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_SaveFailed") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// Feed ids that have a report URL and whose report configuration (URL, secret, or customer id)
    /// changed since load — these should receive an initial snapshot immediately after saving.
    /// </summary>
    private List<string> FeedsWithChangedReportConfig()
    {
        List<string> result = [];
        foreach (FeedViewModel feed in Feeds)
        {
            if (string.IsNullOrWhiteSpace(feed.ReportUrl))
                continue;

            if (!_originalFeedFields.TryGetValue(feed.Id, out FeedFields original))
            {
                result.Add(feed.Id); // newly added feed that already has a report URL
                continue;
            }

            bool reportChanged =
                !string.Equals(original.ReportUrl, feed.ReportUrl, StringComparison.Ordinal)
                || !string.Equals(
                    original.ReportSecret,
                    feed.ReportSecret,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    original.ReportCustomerId,
                    feed.ReportCustomerId,
                    StringComparison.Ordinal
                );
            if (reportChanged)
                result.Add(feed.Id);
        }

        return result;
    }

    /// <summary>
    /// Prompts for the lock password of every locked feed whose editable fields changed since load.
    /// Returns false if the user cancels or enters a wrong password for any of them.
    /// </summary>
    private async Task<bool> AuthorizeLockedFeedChangesAsync()
    {
        FeedUnlockScope scope = _feedLock.CreateScope();
        foreach (FeedViewModel feed in Feeds)
        {
            if (string.IsNullOrEmpty(feed.ExistingLockHash))
                continue;

            bool unchanged =
                _originalFeedFields.TryGetValue(feed.Id, out FeedFields original)
                && original == Capture(feed);
            if (unchanged)
                continue;

            if (
                !await _feedLock.EnsureAuthorizedAsync(
                    feed.Id,
                    LocalizationManager.GetString("FeedLock_Op_SaveChanges"),
                    scope
                )
            )
                return false;
        }

        return true;
    }

    /// <summary>
    /// Re-baselines the in-memory feed state after a successful save so an immediate re-save does
    /// not prompt again: adopts the persisted lock hashes, clears entered lock passwords, and
    /// refreshes the change-detection snapshots.
    /// </summary>
    private void SyncFeedStateAfterSave(FeedConfiguration[] savedFeeds)
    {
        foreach (FeedViewModel feed in Feeds)
        {
            FeedConfiguration? saved = savedFeeds.FirstOrDefault(c => c.Id == feed.Id);
            feed.ExistingLockHash = saved?.LockPasswordHash;
            feed.LockPassword = string.Empty;
        }

        _originalFeedFields.Clear();
        foreach (FeedViewModel feed in Feeds)
            _originalFeedFields[feed.Id] = Capture(feed);
    }

    private static FeedFields Capture(FeedViewModel feed) =>
        new FeedFields(
            feed.Name,
            feed.Url,
            feed.Repository,
            feed.Username,
            feed.Password,
            feed.ReportUrl,
            feed.ReportSecret,
            feed.ReportCustomerId,
            feed.RequireLockPassword,
            feed.LockPassword
        );

    private readonly record struct FeedFields(
        string Name,
        string Url,
        string Repository,
        string Username,
        string Password,
        string ReportUrl,
        string ReportSecret,
        string ReportCustomerId,
        bool RequireLockPassword,
        string LockPassword
    );

    /// <summary>
    /// Determines the lock password hash to persist for a feed: cleared when the lock is
    /// disabled, freshly hashed when a new password is entered, otherwise left unchanged.
    /// </summary>
    /// <summary>
    /// Builds the S3 settings for a feed when its provider is S3, encrypting a newly entered secret key
    /// and otherwise preserving the previously stored one.
    /// </summary>
    private S3FeedSettings? BuildS3Settings(FeedViewModel feed)
    {
        if (feed.Provider != FeedProvider.S3)
            return null;

        string? encryptedSecret = !string.IsNullOrEmpty(feed.S3SecretKey)
            ? _encryptionService.Encrypt(feed.S3SecretKey)
            : feed.ExistingS3EncryptedSecretKey;

        // A white-label feed's S3 coordinates are vendor-fixed; keep them from branding and only take
        // the user-supplied access credentials.
        bool isWhitelabel = Branding.Current.HasFeed && feed.Id == Branding.WhitelabelFeedId;
        if (
            isWhitelabel
            && Branding.Current.Feed is { Provider: FeedProvider.S3, S3: not null } brandFeed
        )
        {
            return BrandingFeedMapper.ToS3Settings(brandFeed, feed.S3AccessKeyId, encryptedSecret);
        }

        return new S3FeedSettings(
            Bucket: feed.S3Bucket.Trim(),
            Region: NullIfBlank(feed.S3Region),
            ServiceUrl: NullIfBlank(feed.S3ServiceUrl),
            UsePathStyle: feed.S3UsePathStyle,
            AccessKeyId: NullIfBlank(feed.S3AccessKeyId),
            EncryptedSecretKey: encryptedSecret,
            Prefix: NullIfBlank(feed.S3Prefix),
            Channels: ParseChannels(feed.S3Channels)
        );
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[]? ParseChannels(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string[] channels = value
            .Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return channels.Length > 0 ? channels : null;
    }

    private static string? ResolveLockHash(FeedViewModel feed)
    {
        if (!feed.RequireLockPassword)
            return null;

        if (!string.IsNullOrEmpty(feed.LockPassword))
            return PasswordHasher.Hash(feed.LockPassword);

        return string.IsNullOrEmpty(feed.ExistingLockHash) ? null : feed.ExistingLockHash;
    }

    /// <summary>
    /// Tests the connection to the specified feed.
    /// </summary>
    /// <param name="feed">The feed to test.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task TestConnectionAsync(FeedViewModel feed)
    {
        if (feed.Provider == FeedProvider.Local)
        {
            await TestLocalConnectionAsync(feed);
            return;
        }

        try
        {
            feed.ConnectionTestMessage = LocalizationManager.GetString("Status_Connecting");
            feed.IsConnectionValid = false;

            FeedConnectionResult result = await Task.Run(() =>
                _connectionService.TestConnectionAsync(feed.Url, feed.Username, feed.Password)
            );

            feed.IsConnectionValid = result.Success;
            feed.ConnectionTestMessage = result.Success
                ? LocalizationManager
                    .GetString("Status_TestSuccess_WithRepos")
                    .Replace("{0}", result.RepositoryCount.ToString())
                : LocalizationManager.GetString("Error_ConnectionFailed")
                    + $" (HTTP {result.HttpStatusCode})";
        }
        catch (Exception ex)
        {
            feed.IsConnectionValid = false;
            feed.ConnectionTestMessage =
                LocalizationManager.GetString("Error_ConnectionError") + ": " + ex.Message;
        }
    }

    private async Task TestLocalConnectionAsync(FeedViewModel feed)
    {
        feed.ConnectionTestMessage = LocalizationManager.GetString("Status_Connecting");
        feed.IsConnectionValid = false;

        string root = feed.Url;
        try
        {
            (bool exists, int products) = await Task.Run(() =>
            {
                if (!Directory.Exists(root))
                    return (false, 0);

                int count = new[] { root }
                    .Concat(Directory.EnumerateDirectories(root))
                    .Count(dir => File.Exists(Path.Combine(dir, "manifest.json")));
                return (true, count);
            });

            feed.IsConnectionValid = exists;
            feed.ConnectionTestMessage = exists
                ? LocalizationManager
                    .GetString("Status_TestSuccess_LocalFolder")
                    .Replace("{0}", products.ToString())
                : LocalizationManager.GetString("Error_ConnectionFailed") + $" ({root})";
        }
        catch (Exception ex)
        {
            feed.IsConnectionValid = false;
            feed.ConnectionTestMessage =
                LocalizationManager.GetString("Error_ConnectionError") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// Exports the current configuration to a file.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ExportAsync()
    {
        string? filePath = _dialogService.ShowSaveFilePicker(
            "JSON files (*.json)|*.json",
            LocalizationManager.GetString("Button_Export")
        );
        if (filePath is null)
            return;

        try
        {
            await Task.Run(() => _configurationService.ExportAsync(filePath));
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_ExportFailed") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// Imports a configuration from a file.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ImportAsync()
    {
        string? filePath = _dialogService.ShowOpenFilePicker(
            "JSON files (*.json)|*.json",
            LocalizationManager.GetString("Button_Import")
        );
        if (filePath is null)
            return;

        try
        {
            await Task.Run(() => _configurationService.ImportAsync(filePath));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_ImportFailed") + ": " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesNowAsync()
    {
        try
        {
            IsCheckingForUpdates = true;
            UpdateCheckResult = string.Empty;

            UpdateInfo? update = await Task.Run(() =>
                _selfUpdateChecker.CheckForUpdateAsync(IncludeDevVersions)
            );

            if (update is not null)
            {
                bool shouldUpdate = false;
                try
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Views.UpdateNotificationDialog dialog = new(
                            update.Version,
                            update.ReleaseNotes ?? ""
                        )
                        {
                            Owner = System.Windows.Application.Current.MainWindow,
                        };
                        shouldUpdate = dialog.ShowDialog() == true;
                    });
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Update notification dialog failed");
                }

                if (shouldUpdate)
                {
                    UpdateCheckResult =
                        LocalizationManager
                            .GetString("Settings_UpdateAvailable")
                            .Replace("{0}", update.Version) + " - Downloading...";

                    await Task.Run(() =>
                        _selfUpdateService.DownloadAndLaunchInstallerAsync(update)
                    );
                }
                else
                {
                    UpdateCheckResult = LocalizationManager
                        .GetString("Settings_UpdateAvailable")
                        .Replace("{0}", update.Version);
                }
            }
            else
            {
                UpdateCheckResult = LocalizationManager.GetString("Settings_UpToDate");
            }
        }
        catch (Exception ex)
        {
            UpdateCheckResult =
                LocalizationManager.GetString("Error_UpdateCheckFailed") + ": " + ex.Message;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }
}

/// <summary>
/// View model for a recommended feed from a plugin.
/// </summary>
public partial class RecommendedFeedViewModel : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _pluginName = string.Empty;

    [ObservableProperty]
    private string _pluginId = string.Empty;
}
