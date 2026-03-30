using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the setup wizard, guiding users through initial configuration.
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IEncryptionService _encryptionService;
    private readonly IFeedConnectionService _connectionService;
    private readonly ILogger<SetupWizardViewModel> _logger;

    public SetupWizardViewModel(
        IConfigurationService configurationService,
        IEncryptionService encryptionService,
        IFeedConnectionService connectionService,
        ILogger<SetupWizardViewModel> logger
    )
    {
        _configurationService = configurationService;
        _encryptionService = encryptionService;
        _connectionService = connectionService;
        _logger = logger;
    }

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private string _feedName = "Default Feed";

    [ObservableProperty]
    private string _feedUrl = string.Empty;

    [ObservableProperty]
    private string _feedRepository = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isConnectionTested;

    [ObservableProperty]
    private bool _isConnectionValid;

    [ObservableProperty]
    private string _connectionTestMessage = string.Empty;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _autoCheckForUpdates = true;

    [ObservableProperty]
    private bool _checkForStorkDropUpdates = true;

    [ObservableProperty]
    private string _proxyHost = string.Empty;

    [ObservableProperty]
    private int _proxyPort;

    /// <summary>
    /// Gets the total number of wizard steps.
    /// </summary>
    public int TotalSteps => 3;

    /// <summary>
    /// Gets a value indicating whether the user can navigate back.
    /// </summary>
    public bool CanGoBack => CurrentStep > 0;

    /// <summary>
    /// Gets a value indicating whether the user can navigate forward.
    /// </summary>
    public bool CanGoNext => CurrentStep < TotalSteps - 1;

    /// <summary>
    /// Gets a value indicating whether the user can finish the wizard.
    /// </summary>
    public bool CanFinish => CurrentStep == TotalSteps - 1;

    /// <summary>
    /// Navigates to the previous wizard step.
    /// </summary>
    [RelayCommand]
    private void GoBack()
    {
        if (CanGoBack)
        {
            CurrentStep--;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanFinish));
        }
    }

    /// <summary>
    /// Navigates to the next wizard step.
    /// </summary>
    [RelayCommand]
    private void GoNext()
    {
        if (CanGoNext)
        {
            CurrentStep++;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanFinish));
        }
    }

    /// <summary>
    /// Tests the connection to the configured feed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            IsConnectionTested = false;
            ConnectionTestMessage = LocalizationManager.GetString("Status_Connecting");

            FeedConnectionResult result = await _connectionService.TestConnectionAsync(
                FeedUrl,
                Username,
                Password
            );

            IsConnectionValid = result.Success;
            ConnectionTestMessage = result.Success
                ? LocalizationManager
                    .GetString("Status_TestSuccess_WithRepos")
                    .Replace("{0}", result.RepositoryCount.ToString())
                : LocalizationManager.GetString("Error_ConnectionFailed")
                    + $" (HTTP {result.HttpStatusCode})";
            IsConnectionTested = true;
        }
        catch (Exception ex)
        {
            IsConnectionValid = false;
            ConnectionTestMessage =
                LocalizationManager.GetString("Error_ConnectionError") + ": " + ex.Message;
            IsConnectionTested = true;
        }
    }

    /// <summary>
    /// Finishes the wizard and saves the configuration.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task FinishAsync()
    {
        FeedConfiguration feed = new FeedConfiguration(
            Id: Guid.NewGuid().ToString(),
            Name: FeedName,
            Url: FeedUrl,
            Repository: !string.IsNullOrWhiteSpace(FeedRepository) ? FeedRepository : null,
            Username: Username,
            EncryptedPassword: !string.IsNullOrEmpty(Password)
                ? _encryptionService.Encrypt(Password)
                : null,
            PluginId: null
        );

        ProxySettings? proxy = !string.IsNullOrEmpty(ProxyHost)
            ? new ProxySettings(ProxyHost, ProxyPort)
            : null;

        AppConfiguration config = new AppConfiguration(
            Feeds: new FeedConfiguration[] { feed },
            AutoStart: AutoStart,
            AutoCheckForUpdates: AutoCheckForUpdates,
            CheckInterval: TimeSpan.FromHours(4),
            ProxySettings: proxy,
            Language: LocalizationManager.Language,
            CheckForStorkDropUpdates: CheckForStorkDropUpdates
        );

        await _configurationService.SaveAsync(config);
    }
}
