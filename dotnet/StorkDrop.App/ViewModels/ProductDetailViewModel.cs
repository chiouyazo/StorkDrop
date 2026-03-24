using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Core.Interfaces;
using StorkDrop.Core.Models;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the product detail view, displaying product information and handling installation.
/// </summary>
public partial class ProductDetailViewModel : ObservableObject
{
    private readonly IRegistryClient _registryClient;
    private readonly IInstallationEngine _installationEngine;
    private readonly IProductRepository _productRepository;
    private readonly DialogService _dialogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductDetailViewModel"/> class.
    /// </summary>
    /// <param name="registryClient">The registry client for fetching product details.</param>
    /// <param name="installationEngine">The engine for installing products.</param>
    /// <param name="productRepository">The repository for installed products.</param>
    /// <param name="dialogService">The dialog service for user interactions.</param>
    public ProductDetailViewModel(
        IRegistryClient registryClient,
        IInstallationEngine installationEngine,
        IProductRepository productRepository,
        DialogService dialogService
    )
    {
        _registryClient = registryClient;
        _installationEngine = installationEngine;
        _productRepository = productRepository;
        _dialogService = dialogService;
    }

    [ObservableProperty]
    private ProductManifest? _manifest;

    [ObservableProperty]
    private ObservableCollection<string> _availableVersions = new ObservableCollection<string>();

    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    [ObservableProperty]
    private string _installPath = string.Empty;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private int _installProgress;

    [ObservableProperty]
    private string _installStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installedVersion = string.Empty;

    [ObservableProperty]
    private string _installButtonText = string.Empty;

    [ObservableProperty]
    private bool _isSelectedVersionInstalled;

    [ObservableProperty]
    private string _selectedVersionReleaseNotes = string.Empty;

    public bool CanInstallSelectedVersion => !IsInstalling && !IsSelectedVersionInstalled;

    /// <summary>
    /// Event raised when the user wants to go back to the marketplace.
    /// </summary>
    public event Action? GoBackRequested;

    partial void OnIsInstallingChanged(bool value) =>
        OnPropertyChanged(nameof(CanInstallSelectedVersion));

    partial void OnIsSelectedVersionInstalledChanged(bool value) =>
        OnPropertyChanged(nameof(CanInstallSelectedVersion));

    partial void OnSelectedVersionChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && Manifest is not null)
        {
            _ = LoadVersionReleaseNotesAsync(value);
            UpdateInstallButtonText();
        }
    }

    private void UpdateInstallButtonText()
    {
        if (IsInstalled && SelectedVersion == InstalledVersion)
        {
            IsSelectedVersionInstalled = true;
            InstallButtonText = LocalizationManager.GetString("Install_Installed") + " v" + SelectedVersion;
        }
        else
        {
            IsSelectedVersionInstalled = false;
            InstallButtonText = LocalizationManager.GetString("Install_Button") + " v" + SelectedVersion;
        }
    }

    /// <summary>
    /// Loads the product details for the specified product ID.
    /// </summary>
    /// <param name="productId">The product ID to load details for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task LoadAsync(string productId)
    {
        Manifest = await _registryClient.GetProductManifestAsync(productId);
        if (Manifest is null)
            return;

        IReadOnlyList<string> versions = await _registryClient.GetAvailableVersionsAsync(productId);
        AvailableVersions = new ObservableCollection<string>(versions);
        SelectedVersion = Manifest.Version;
        InstallPath = Manifest.RecommendedInstallPath ?? string.Empty;
        SelectedVersionReleaseNotes = Manifest.ReleaseNotes ?? string.Empty;

        InstalledProduct? installed = await _productRepository.GetByIdAsync(productId);
        IsInstalled = installed is not null;
        InstalledVersion = installed?.Version ?? string.Empty;
        UpdateInstallButtonText();
    }

    private async Task LoadVersionReleaseNotesAsync(string version)
    {
        if (Manifest is null)
            return;

        if (version == Manifest.Version)
        {
            SelectedVersionReleaseNotes = Manifest.ReleaseNotes ?? string.Empty;
            return;
        }

        try
        {
            ProductManifest? versionManifest = await _registryClient.GetProductManifestAsync(
                Manifest.ProductId,
                version
            );
            SelectedVersionReleaseNotes = versionManifest?.ReleaseNotes ?? string.Empty;
        }
        catch
        {
            SelectedVersionReleaseNotes = string.Empty;
        }
    }

    /// <summary>
    /// Opens a folder picker to select the install directory.
    /// </summary>
    [RelayCommand]
    private void BrowseInstallPath()
    {
        string? path = _dialogService.ShowFolderPicker(LocalizationManager.GetString("Install_Directory"));
        if (path is not null)
            InstallPath = path;
    }

    /// <summary>
    /// Navigates back to the marketplace view.
    /// </summary>
    [RelayCommand]
    private void GoBack()
    {
        GoBackRequested?.Invoke();
    }

    /// <summary>
    /// Installs the product with the selected version and install path.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (Manifest is null || string.IsNullOrEmpty(InstallPath))
            return;

        try
        {
            IsInstalling = true;
            InstallOptions options = new InstallOptions(TargetPath: InstallPath);
            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                InstallProgress = p.Percentage;
                InstallStatusMessage = p.Message;
            });

            ProductManifest? versionManifest =
                SelectedVersion == Manifest.Version
                    ? Manifest
                    : await _registryClient.GetProductManifestAsync(
                        Manifest.ProductId,
                        SelectedVersion
                    );

            if (versionManifest is not null)
            {
                InstallResult result = await _installationEngine.InstallAsync(
                    versionManifest,
                    options,
                    progress
                );

                if (result.Success)
                {
                    IsInstalled = true;
                    InstalledVersion = SelectedVersion;
                    UpdateInstallButtonText();
                }
                else
                {
                    _dialogService.ShowError(
                        LocalizationManager.GetString("Error_InstallFailed_Generic")
                            + ": "
                            + (result.ErrorMessage ?? string.Empty)
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(
                LocalizationManager.GetString("Error_InstallFailed_Generic") + ": " + ex.Message
            );
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
