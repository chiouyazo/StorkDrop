using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;
using StorkDrop.Core.Interfaces;
using StorkDrop.Core.Models;
using StorkDrop.Registry;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the settings view, managing feeds, proxy, language, and application preferences.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly DialogService _dialogService;
    private readonly IEnumerable<IStorkDropPlugin> _plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="configurationService">The configuration service for loading and saving settings.</param>
    /// <param name="encryptionService">The encryption service for securing passwords.</param>
    /// <param name="dialogService">The dialog service for user interactions.</param>
    /// <param name="plugins">The loaded plugins to get recommended feeds from.</param>
    public SettingsViewModel(
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        DialogService dialogService,
        IEnumerable<IStorkDropPlugin> plugins
    )
    {
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _dialogService = dialogService;
        _plugins = plugins;

        BuildRecommendedFeeds();
    }

    [ObservableProperty]
    private ObservableCollection<FeedViewModel> _feeds = new ObservableCollection<FeedViewModel>();

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
            AppConfiguration? config = await _configurationService.LoadAsync();
            if (config is null)
                return;

            AutoStart = config.AutoStart;
            AutoCheckForUpdates = config.AutoCheckForUpdates;
            CheckIntervalHours = (int)config.CheckInterval.TotalHours;

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
                        catch
                        {
                            decryptedPassword = string.Empty;
                        }
                    }

                    return new FeedViewModel
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Url = f.Url,
                        Repository = f.Repository,
                        Username = f.Username ?? string.Empty,
                        Password = decryptedPassword,
                        PluginId = f.PluginId ?? string.Empty,
                    };
                })
            );
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

            FeedConfiguration[] feeds = Feeds
                .Select(f => new FeedConfiguration(
                    f.Id,
                    f.Name,
                    f.Url,
                    f.Repository,
                    !string.IsNullOrEmpty(f.Username) ? f.Username : null,
                    !string.IsNullOrEmpty(f.Password)
                        ? _encryptionService.Encrypt(f.Password)
                        : null,
                    !string.IsNullOrEmpty(f.PluginId) ? f.PluginId : null
                ))
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
                LogLevel: SelectedLogLevel
            );

            await _configurationService.SaveAsync(config);

            // Reload NexusOptions so marketplace uses new credentials
            if (feeds.Length > 0)
            {
                FeedConfiguration firstFeed = feeds[0];
                NexusOptions nexusOptions = App
                    .Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<NexusOptions>>()
                    .Value;
                nexusOptions.BaseUrl = firstFeed.Url;
                nexusOptions.Repository = firstFeed.Repository;
                nexusOptions.Username = firstFeed.Username ?? string.Empty;
                nexusOptions.Password = !string.IsNullOrEmpty(firstFeed.EncryptedPassword)
                    ? _encryptionService.Decrypt(firstFeed.EncryptedPassword)
                    : string.Empty;
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_SaveFailed") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// Tests the connection to the specified feed.
    /// </summary>
    /// <param name="feed">The feed to test.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task TestConnectionAsync(FeedViewModel feed)
    {
        try
        {
            feed.ConnectionTestMessage = LocalizationManager.GetString("Status_Connecting");
            feed.IsConnectionValid = false;

            HttpClientHandler handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            using HttpClient testClient = new HttpClient(handler);
            string baseUrl = feed.Url.TrimEnd('/');
            if (!string.IsNullOrEmpty(feed.Username) && !string.IsNullOrEmpty(feed.Password))
            {
                string credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{feed.Username}:{feed.Password}")
                );
                testClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    credentials
                );
            }

            using CancellationTokenSource cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(15)
            );
            HttpResponseMessage response = await testClient.GetAsync(
                $"{baseUrl}/service/rest/v1/repositories",
                cts.Token
            );
            feed.IsConnectionValid = response.IsSuccessStatusCode;
            feed.ConnectionTestMessage = feed.IsConnectionValid
                ? LocalizationManager.GetString("Status_TestSuccess")
                : LocalizationManager.GetString("Error_ConnectionFailed")
                    + $" (HTTP {(int)response.StatusCode})";
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
            await _configurationService.ExportAsync(filePath);
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
            await _configurationService.ImportAsync(filePath);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_ImportFailed") + ": " + ex.Message;
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
