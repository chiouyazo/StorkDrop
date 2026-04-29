using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using ChannelEntry = (
    StorkDrop.Contracts.Models.ProductManifest Manifest,
    string FeedName,
    string FeedId
);

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the marketplace view, displaying available products and handling installation.
/// </summary>
public partial class MarketplaceViewModel : ObservableObject
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly IProductRepository _productRepository;
    private readonly InstallationCoordinator _coordinator;
    private readonly InstallationTracker _tracker;
    private readonly INotificationService _notificationService;
    private readonly PostProductResolver _postProductResolver;
    private readonly ILogger<MarketplaceViewModel> _logger;

    public MarketplaceViewModel(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        InstallationCoordinator coordinator,
        InstallationTracker tracker,
        INotificationService notificationService,
        PostProductResolver postProductResolver,
        ILogger<MarketplaceViewModel> logger
    )
    {
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _tracker = tracker;
        _coordinator = coordinator;
        _notificationService = notificationService;
        _postProductResolver = postProductResolver;
        _logger = logger;
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
    public bool ShowFeedFilter => AvailableFeedFilters.Count > 1;

    private List<(ProductManifest Manifest, string FeedName, string FeedId)> _allProducts = [];
    private Dictionary<string, List<ChannelEntry>> _channelsByProduct = new();
    private List<ProductCardViewModel> _allCards = [];
    private IReadOnlyList<InstalledProduct> _installedProducts = Array.Empty<InstalledProduct>();
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Event raised when the user wants to navigate to a product detail view.
    /// </summary>
    public event Action<string>? NavigateToProductDetail;

    /// <summary>
    /// Event raised when the user wants to navigate to the installed view for a product.
    /// </summary>
    public event Action<string>? NavigateToManageProduct;

    private CancellationTokenSource? _searchDebounce;

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        CancellationToken token = _searchDebounce.Token;

        Task.Delay(250, token)
            .ContinueWith(
                _ =>
                {
                    if (!token.IsCancellationRequested)
                        System.Windows.Application.Current?.Dispatcher.Invoke(ApplyFilters);
                },
                TaskScheduler.Default
            );
    }

    partial void OnSelectedFilterNameChanged(string? value) => ApplyFilters();

    partial void OnAvailableFeedFiltersChanged(ObservableCollection<string> value) =>
        OnPropertyChanged(nameof(ShowFeedFilter));

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

    private static string ExtractBaseFeedName(string feedName)
    {
        int separatorIndex = feedName.IndexOf(" / ", StringComparison.Ordinal);
        return separatorIndex > 0 ? feedName[..separatorIndex] : feedName;
    }

    [RelayCommand]
    private void ManageProduct(ProductCardViewModel product)
    {
        NavigateToManageProduct?.Invoke(product.ProductId);
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
            IRegistryClient feedClient = _feedRegistry.GetClient(product.FeedId);
            ProductManifest? manifest = await feedClient.GetProductManifestAsync(product.ProductId);
            if (manifest is null)
            {
                ErrorMessage = LocalizationManager
                    .GetString("Error_ManifestNotFound")
                    .Replace("{0}", product.Title);
                return;
            }

            string defaultPath =
                manifest.RecommendedInstallPath
                ?? Path.Combine(StorkPaths.DefaultInstallRoot, product.Title);

            bool hasFileTypeHandlers = App
                .Services.GetServices<IStorkDropPlugin>()
                .Any(p => p is IFileTypeHandler);

            Views.InstallDialog dialog = new Views.InstallDialog(
                product.Title,
                product.Version,
                defaultPath,
                manifest,
                hasFileTypeHandlers
            );
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            bool? result = dialog.ShowDialog();

            if (result != true || !dialog.Confirmed)
                return;

            string targetPath = dialog.SelectedPath;

            // Check required components
            if (manifest.RequiredProductIds is { Length: > 0 })
            {
                OptionalPostProduct[] requiredAsOptional = manifest
                    .RequiredProductIds.Select(id => new OptionalPostProduct(
                        id,
                        HideNoAccess: false
                    ))
                    .ToArray();

                PostProductResolution resolution = await _postProductResolver.ResolveAsync(
                    requiredAsOptional,
                    manifest.BadgeText
                );

                if (resolution.Available.Count > 0 || resolution.Warnings.Count > 0)
                {
                    Views.RequiredProductsDialog reqDialog = new(
                        product.Title,
                        resolution.Available,
                        resolution.AlreadyInstalled,
                        resolution.Warnings
                    )
                    {
                        Owner = System.Windows.Application.Current.MainWindow,
                    };

                    if (reqDialog.ShowDialog() != true)
                        return;

                    foreach (ResolvedPostProduct reqProduct in reqDialog.SelectedProducts)
                    {
                        await InstallPostProductAsync(reqProduct);

                        IReadOnlyList<InstalledProduct> checkInstances =
                            await _productRepository.GetInstancesAsync(
                                reqProduct.Manifest.ProductId
                            );
                        if (checkInstances.Count == 0)
                        {
                            _logger.LogWarning(
                                "Required product {ProductId} was not installed successfully, aborting",
                                reqProduct.Manifest.ProductId
                            );
                            return;
                        }
                    }
                }
            }

            product.IsInstalling = true;
            product.InstallPercentage = 0;

            TrackedInstallation tracked = _tracker.StartInstallation(
                product.ProductId,
                product.Title
            );
            tracked.AddLog($"Installing {product.Title} v{product.Version} to {targetPath}");

            InstallOptions options = new InstallOptions(
                TargetPath: targetPath,
                FeedId: product.FeedId
            );
            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                product.InstallPercentage = p.Percentage;
                product.InstallStatusMessage = p.Message;
                tracked.Percentage = p.Percentage;
                tracked.StatusMessage = p.Message;
                if (!string.IsNullOrEmpty(p.Message))
                    tracked.AddLog(p.Message);
            });

            InstallResult installResult = await _coordinator.InstallWithIsolationAsync(
                manifest,
                options,
                progress,
                tracked.Cts.Token
            );

            if (!installResult.Success)
            {
                tracked.Complete(false, installResult.ErrorMessage);
                _tracker.NotifyChanged();
                ErrorMessage =
                    LocalizationManager
                        .GetString("Error_InstallFailed")
                        .Replace("{0}", product.Title)
                    + ": "
                    + (installResult.ErrorMessage ?? string.Empty);
                product.IsInstalling = false;
                try
                {
                    _notificationService.ShowError(
                        $"Installation of {product.Title} failed",
                        "Click the installation indicator in the status bar to view details."
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to show error notification for {ProductTitle}",
                        product.Title
                    );
                }
                return;
            }

            tracked.Complete(true);
            _tracker.NotifyChanged();
            try
            {
                _notificationService.ShowSuccess(
                    $"{product.Title} installed successfully",
                    $"Version {product.Version} has been installed."
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to show success notification for {ProductTitle}",
                    product.Title
                );
            }
            product.IsInstalled = true;
            product.HasUpdate = false;
            product.InstalledVersion = product.Version;
            product.IsInstalling = false;

            // Offer optional post-products
            await HandleOptionalPostProductsAsync(manifest);

            // If installed to StorkDrop's own directory, prompt for restart
            if (manifest.RecommendedInstallPath?.Contains("{StorkPath}") == true)
            {
                System.Windows.MessageBoxResult restartResult = System.Windows.MessageBox.Show(
                    Localization
                        .LocalizationManager.GetString("Restart_PluginInstalled")
                        .Replace("{0}", product.Title),
                    "StorkDrop",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question
                );
                if (restartResult == System.Windows.MessageBoxResult.Yes)
                {
                    string? exePath = System.Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // Use cmd /c with a delay so the current process exits and releases the mutex first
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c timeout /t 2 /nobreak >nul & \"{exePath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            }
                        );
                    }
                    System.Windows.Application.Current.Shutdown();
                }
            }
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

    private async Task HandleOptionalPostProductsAsync(ProductManifest parentManifest)
    {
        if (parentManifest.OptionalPostProducts is not { Length: > 0 })
            return;

        try
        {
            PostProductResolution resolution = await _postProductResolver.ResolveAsync(
                parentManifest.OptionalPostProducts,
                parentManifest.BadgeText
            );

            foreach (string warning in resolution.Warnings)
            {
                try
                {
                    _notificationService.ShowInfo(
                        LocalizationManager
                            .GetString("OptionalProducts_NotAvailable")
                            .Replace("{0}", warning),
                        string.Empty
                    );
                }
                catch { }
            }

            if (resolution.Available.Count == 0)
                return;

            Views.OptionalPostProductsDialog dialog = new(
                parentManifest.Title,
                resolution.Available,
                resolution.AlreadyInstalled
            )
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };

            if (dialog.ShowDialog() != true || dialog.SelectedProducts.Count == 0)
                return;

            foreach (ResolvedPostProduct postProduct in dialog.SelectedProducts)
            {
                try
                {
                    await InstallPostProductAsync(postProduct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to install optional post-product {ProductId}",
                        postProduct.Manifest.ProductId
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to handle optional post-products for {ProductId}",
                parentManifest.ProductId
            );
        }
    }

    private async Task InstallPostProductAsync(ResolvedPostProduct postProduct)
    {
        ProductManifest manifest = postProduct.Manifest;

        string defaultPath =
            manifest.RecommendedInstallPath
            ?? Path.Combine(StorkPaths.DefaultInstallRoot, manifest.Title);

        bool hasFileTypeHandlers = App
            .Services.GetServices<IStorkDropPlugin>()
            .Any(p => p is IFileTypeHandler);

        Views.InstallDialog dialog = new Views.InstallDialog(
            manifest.Title,
            manifest.Version,
            defaultPath,
            manifest,
            hasFileTypeHandlers
        );
        dialog.Owner = System.Windows.Application.Current.MainWindow;

        if (dialog.ShowDialog() != true || !dialog.Confirmed)
            return;

        string targetPath = dialog.SelectedPath;

        TrackedInstallation tracked = _tracker.StartInstallation(
            manifest.ProductId,
            manifest.Title
        );
        tracked.AddLog($"Installing {manifest.Title} v{manifest.Version} to {targetPath}");

        InstallOptions options = new InstallOptions(
            TargetPath: targetPath,
            FeedId: postProduct.FeedId
        );
        Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
        {
            tracked.Percentage = p.Percentage;
            tracked.StatusMessage = p.Message;
            if (!string.IsNullOrEmpty(p.Message))
                tracked.AddLog(p.Message);
        });

        InstallResult installResult = await _coordinator.InstallWithIsolationAsync(
            manifest,
            options,
            progress,
            tracked.Cts.Token
        );

        if (!installResult.Success)
        {
            tracked.Complete(false, installResult.ErrorMessage);
            _tracker.NotifyChanged();
            _logger.LogWarning(
                "Optional post-product {ProductId} installation failed: {Error}",
                manifest.ProductId,
                installResult.ErrorMessage
            );
            try
            {
                _notificationService.ShowError(
                    $"Installation of {manifest.Title} failed",
                    installResult.ErrorMessage ?? string.Empty
                );
            }
            catch
            {
                // Notification failures should never block
            }
            return;
        }

        tracked.Complete(true);
        _tracker.NotifyChanged();
        try
        {
            _notificationService.ShowSuccess(
                $"{manifest.Title} installed successfully",
                $"Version {manifest.Version} has been installed."
            );
        }
        catch
        {
            // Notification failures should never block
        }

        ProductCardViewModel? card = Products.FirstOrDefault(p =>
            p.ProductId == manifest.ProductId
        );
        if (card is not null)
        {
            card.IsInstalled = true;
            card.HasUpdate = false;
            card.InstalledVersion = manifest.Version;
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

            _installedProducts = await _productRepository.GetAllAsync(cancellationToken);

            // Load products from all configured feeds
            _allProducts = [];
            ObservableCollection<string> feedFilters = new ObservableCollection<string>([
                LocalizationManager.GetString("Filter_AllFeeds"),
            ]);

            foreach (FeedInfo feed in _feedRegistry.GetFeeds())
            {
                try
                {
                    IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                    IReadOnlyList<ProductManifest> products = await client.GetAllProductsAsync(
                        cancellationToken
                    );
                    foreach (ProductManifest p in products)
                        _allProducts.Add((p, feed.Name, feed.Id));

                    if (!feedFilters.Contains(feed.Name))
                        feedFilters.Add(feed.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to load products from feed {FeedName}",
                        feed.Name
                    );
                }
            }

            AvailableFeedFilters = feedFilters;
            SelectedFeedFilter = LocalizationManager.GetString("Filter_AllFeeds");

            // Build type filters
            ObservableCollection<string> typeFilters = new ObservableCollection<string>([
                LocalizationManager.GetString("Filter_AllTypes"),
            ]);
            foreach ((ProductManifest product, _, _) in _allProducts)
            {
                string typeName = product.InstallType.ToString();
                if (!typeFilters.Contains(typeName))
                    typeFilters.Add(typeName);
            }

            AvailableFilters = typeFilters;
            SelectedFilterName = LocalizationManager.GetString("Filter_AllTypes");

            // Build publisher filters
            ObservableCollection<string> publisherFilters = new ObservableCollection<string>([
                LocalizationManager.GetString("Filter_AllPublishers"),
            ]);
            foreach ((ProductManifest product, _, _) in _allProducts)
            {
                if (
                    !string.IsNullOrEmpty(product.Publisher)
                    && !publisherFilters.Contains(product.Publisher)
                )
                    publisherFilters.Add(product.Publisher);
            }

            AvailablePublisherFilters = publisherFilters;
            SelectedPublisherFilter = LocalizationManager.GetString("Filter_AllPublishers");

            RebuildCardCache();
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

    private void RebuildCardCache()
    {
        _channelsByProduct = _allProducts
            .GroupBy(entry => entry.Manifest.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        _allCards = _channelsByProduct
            .Select(group =>
            {
                List<ChannelEntry> channels = group.Value;
                ChannelEntry best = channels
                    .OrderByDescending(c => c.Manifest.Version, VersionComparer.Instance)
                    .First();
                ProductManifest manifest = best.Manifest;

                IEnumerable<InstalledProduct> instances = _installedProducts.Where(i =>
                    i.ProductId == group.Key
                );
                int instanceCount = instances.Count();
                bool isInstalled = instanceCount > 0;
                InstalledProduct? installed = instances.FirstOrDefault();
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
                    FeedName = ExtractBaseFeedName(best.FeedName),
                    FeedId = best.FeedId,
                    BadgeText = null,
                    BadgeColor = null,
                    ChannelCount = channels.Count,
                    InstanceCount = instanceCount,
                    AllowMultipleInstances = manifest.AllowMultipleInstances,
                };

                if (!string.IsNullOrEmpty(manifest.ImageUrl))
                    _ = card.LoadImageAsync();

                return card;
            })
            .ToList();
    }

    private void ApplyFilters()
    {
        IEnumerable<ProductCardViewModel> filtered = _allCards;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || c.ProductId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || c.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (
            SelectedFeedFilter is not null
            && SelectedFeedFilter != LocalizationManager.GetString("Filter_AllFeeds")
        )
        {
            filtered = filtered.Where(c =>
                _channelsByProduct.TryGetValue(c.ProductId, out List<ChannelEntry>? channels)
                && channels.Any(ch => ch.FeedName == SelectedFeedFilter)
            );
        }

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
            filtered = filtered.Where(c => c.InstallType == filterType);
        }

        if (
            SelectedPublisherFilter is not null
            && SelectedPublisherFilter != LocalizationManager.GetString("Filter_AllPublishers")
        )
        {
            filtered = filtered.Where(c => c.Publisher == SelectedPublisherFilter);
        }

        Products = new ObservableCollection<ProductCardViewModel>(filtered);
    }
}
