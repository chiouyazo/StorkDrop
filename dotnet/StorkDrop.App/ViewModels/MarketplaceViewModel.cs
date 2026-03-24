using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the marketplace view, displaying available products and handling installation.
/// </summary>
public partial class MarketplaceViewModel : ObservableObject
{
    private readonly IRegistryClient _registryClient;
    private readonly IProductRepository _productRepository;
    private readonly IInstallationEngine _installationEngine;
    private readonly IConfigurationService _configurationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarketplaceViewModel"/> class.
    /// </summary>
    /// <param name="registryClient">The registry client for fetching products.</param>
    /// <param name="productRepository">The repository for installed products.</param>
    /// <param name="installationEngine">The engine for installing products.</param>
    /// <param name="configurationService">The configuration service.</param>
    public MarketplaceViewModel(
        IRegistryClient registryClient,
        IProductRepository productRepository,
        IInstallationEngine installationEngine,
        IConfigurationService configurationService
    )
    {
        _registryClient = registryClient;
        _productRepository = productRepository;
        _installationEngine = installationEngine;
        _configurationService = configurationService;
    }

    [ObservableProperty]
    private ObservableCollection<ProductCardViewModel> _products =
        new ObservableCollection<ProductCardViewModel>();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _selectedFilterName;

    [ObservableProperty]
    private ObservableCollection<string> _availableFilters = new ObservableCollection<string>();

    [ObservableProperty]
    private string? _selectedFeedFilter;

    [ObservableProperty]
    private ObservableCollection<string> _availableFeedFilters = new ObservableCollection<string>();

    [ObservableProperty]
    private string? _selectedPublisherFilter;

    [ObservableProperty]
    private ObservableCollection<string> _availablePublisherFilters =
        new ObservableCollection<string>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets a value indicating whether an error message is currently displayed.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private IReadOnlyList<ProductManifest> _allProducts = Array.Empty<ProductManifest>();
    private IReadOnlyList<InstalledProduct> _installedProducts = Array.Empty<InstalledProduct>();
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Event raised when the user wants to navigate to a product detail view.
    /// </summary>
    public event Action<string>? NavigateToProductDetail;

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedFilterNameChanged(string? value) => ApplyFilters();

    partial void OnSelectedFeedFilterChanged(string? value) => ApplyFilters();

    partial void OnSelectedPublisherFilterChanged(string? value) => ApplyFilters();

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>
    /// Navigates to the product detail view for the specified product.
    /// </summary>
    /// <param name="product">The product card view model to show details for.</param>
    [RelayCommand]
    private void NavigateToDetail(ProductCardViewModel product)
    {
        NavigateToProductDetail?.Invoke(product.ProductId);
    }

    /// <summary>
    /// Installs the specified product from the marketplace.
    /// </summary>
    /// <param name="product">The product card view model to install.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task InstallProductAsync(ProductCardViewModel product)
    {
        try
        {
            ProductManifest? manifest = await _registryClient.GetProductManifestAsync(
                product.ProductId
            );
            if (manifest is null)
            {
                ErrorMessage = LocalizationManager
                    .GetString("Error_ManifestNotFound")
                    .Replace("{0}", product.Title);
                return;
            }

            string defaultPath =
                manifest.RecommendedInstallPath
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "StorkDrop",
                    product.Title
                );

            Views.InstallDialog dialog = new Views.InstallDialog(
                product.Title,
                product.Version,
                defaultPath,
                manifest
            );
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            bool? result = dialog.ShowDialog();

            if (result != true || !dialog.Confirmed)
                return;

            product.IsInstalling = true;
            product.InstallPercentage = 0;
            string targetPath = dialog.SelectedPath;

            InstallOptions options = new InstallOptions(TargetPath: targetPath);
            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                product.InstallPercentage = p.Percentage;
                product.InstallStatusMessage = p.Message;
            });

            InstallResult installResult = await _installationEngine.InstallAsync(
                manifest,
                options,
                progress
            );

            if (!installResult.Success)
            {
                ErrorMessage =
                    LocalizationManager
                        .GetString("Error_InstallFailed")
                        .Replace("{0}", product.Title)
                    + ": "
                    + (installResult.ErrorMessage ?? string.Empty);
                product.IsInstalling = false;
                return;
            }

            product.IsInstalled = true;
            product.IsInstalling = false;
        }
        catch (OperationCanceledException)
        {
            product.IsInstalling = false;
        }
        catch (Exception ex)
        {
            ErrorMessage =
                LocalizationManager.GetString("Error_InstallFailed").Replace("{0}", product.Title)
                + ": "
                + ex.Message;
            product.IsInstalling = false;
        }
    }

    /// <summary>
    /// Loads all products from the registry and installed product list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            CancellationToken cancellationToken = _loadCts.Token;

            IsLoading = true;
            ErrorMessage = string.Empty;

            _allProducts = await _registryClient.GetAllProductsAsync(cancellationToken);
            _installedProducts = await _productRepository.GetAllAsync(cancellationToken);

            // Build type filters
            ObservableCollection<string> typeFilters = new ObservableCollection<string>(
                new List<string> { LocalizationManager.GetString("Filter_AllTypes") }
            );
            foreach (ProductManifest product in _allProducts)
            {
                string typeName = product.InstallType.ToString();
                if (!typeFilters.Contains(typeName))
                {
                    typeFilters.Add(typeName);
                }
            }

            AvailableFilters = typeFilters;
            SelectedFilterName = LocalizationManager.GetString("Filter_AllTypes");

            // Build feed filters
            AppConfiguration? config = await _configurationService.LoadAsync(cancellationToken);
            ObservableCollection<string> feedFilters = new ObservableCollection<string>(
                new List<string> { LocalizationManager.GetString("Filter_AllFeeds") }
            );
            if (config?.Feeds is not null)
            {
                foreach (FeedConfiguration feed in config.Feeds)
                {
                    if (!feedFilters.Contains(feed.Name))
                    {
                        feedFilters.Add(feed.Name);
                    }
                }
            }

            AvailableFeedFilters = feedFilters;
            SelectedFeedFilter = LocalizationManager.GetString("Filter_AllFeeds");

            // Build publisher filters
            ObservableCollection<string> publisherFilters = new ObservableCollection<string>(
                new List<string> { LocalizationManager.GetString("Filter_AllPublishers") }
            );
            foreach (ProductManifest product in _allProducts)
            {
                if (
                    !string.IsNullOrEmpty(product.Publisher)
                    && !publisherFilters.Contains(product.Publisher)
                )
                {
                    publisherFilters.Add(product.Publisher);
                }
            }

            AvailablePublisherFilters = publisherFilters;
            SelectedPublisherFilter = LocalizationManager.GetString("Filter_AllPublishers");

            ApplyFilters();
        }
        catch (OperationCanceledException)
        {
            // Expected when reloading
        }
        catch (Exception ex)
        {
            ErrorMessage =
                LocalizationManager.GetString("Error_LoadProductsFailed") + ": " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<ProductManifest> filtered = _allProducts;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(p =>
                p.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || p.ProductId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (p.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            );
        }

        // Type filter
        if (
            SelectedFilterName is not null
            && SelectedFilterName != LocalizationManager.GetString("Filter_AllTypes")
            && Enum.TryParse<InstallType>(
                SelectedFilterName,
                ignoreCase: true,
                out InstallType filterType
            )
        )
        {
            filtered = filtered.Where(p => p.InstallType == filterType);
        }

        // Publisher filter
        if (
            SelectedPublisherFilter is not null
            && SelectedPublisherFilter != LocalizationManager.GetString("Filter_AllPublishers")
        )
        {
            filtered = filtered.Where(p => p.Publisher == SelectedPublisherFilter);
        }

        Products = new ObservableCollection<ProductCardViewModel>(
            filtered.Select(manifest =>
            {
                InstalledProduct? installed = _installedProducts.FirstOrDefault(i =>
                    i.ProductId == manifest.ProductId
                );
                bool isInstalled = installed is not null;
                bool hasUpdate =
                    isInstalled && VersionComparer.IsNewer(manifest.Version, installed!.Version);

                ProductCardViewModel card = new ProductCardViewModel
                {
                    ProductId = manifest.ProductId,
                    Title = manifest.Title,
                    Version = manifest.Version,
                    InstallType = manifest.InstallType,
                    Description = manifest.Description ?? string.Empty,
                    IsInstalled = isInstalled,
                    HasUpdate = hasUpdate,
                    InstalledVersion = isInstalled ? installed!.Version : string.Empty,
                    ImageUrl = manifest.ImageUrl,
                    Publisher = manifest.Publisher,
                };

                // Fire and forget image loading
                if (!string.IsNullOrEmpty(manifest.ImageUrl))
                {
                    _ = card.LoadImageAsync();
                }

                return card;
            })
        );
    }
}
