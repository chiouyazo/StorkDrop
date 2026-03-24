using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the setup wizard, guiding users through initial configuration.
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IRegistryClient _registryClient;
    private readonly IEncryptionService _encryptionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupWizardViewModel"/> class.
    /// </summary>
    /// <param name="configurationService">The configuration service for saving settings.</param>
    /// <param name="registryClient">The registry client for testing connections.</param>
    /// <param name="encryptionService">The encryption service for securing passwords.</param>
    public SetupWizardViewModel(
        IConfigurationService configurationService,
        IRegistryClient registryClient,
        IEncryptionService encryptionService
    )
    {
        _configurationService = configurationService;
        _registryClient = registryClient;
        _encryptionService = encryptionService;
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

            HttpClientHandler handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            using HttpClient testClient = new HttpClient(handler);
            string baseUrl = FeedUrl.TrimEnd('/');
            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                string credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{Username}:{Password}")
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
            IsConnectionValid = response.IsSuccessStatusCode;
            ConnectionTestMessage = IsConnectionValid
                ? LocalizationManager.GetString("Status_TestSuccess")
                : LocalizationManager.GetString("Error_ConnectionFailed")
                    + $" (HTTP {(int)response.StatusCode})";
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
            Repository: FeedRepository,
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
            Language: LocalizationManager.Language
        );

        await _configurationService.SaveAsync(config);
    }
}
