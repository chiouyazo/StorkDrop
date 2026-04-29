using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;

namespace StorkDrop.App.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly IProductRepository _productRepository;
    private readonly InstallationCoordinator _coordinator;
    private readonly ILogger<UpdatesViewModel> _logger;

    public UpdatesViewModel(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        InstallationCoordinator coordinator,
        ILogger<UpdatesViewModel> logger
    )
    {
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _coordinator = coordinator;
        _logger = logger;
    }

    [ObservableProperty]
    private ObservableCollection<UpdateItemViewModel> _updates = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isUpdatingAll;

    private CancellationTokenSource? _cts;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken cancellationToken = _cts.Token;

            IsLoading = true;
            ErrorMessage = string.Empty;
            IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync(
                cancellationToken
            );

            Dictionary<string, (ProductManifest Manifest, string FeedId)> latestByProduct =
                new Dictionary<string, (ProductManifest Manifest, string FeedId)>();
            foreach (FeedInfo feed in _feedRegistry.GetFeeds())
            {
                try
                {
                    IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                    IReadOnlyList<ProductManifest> available = await client.GetAllProductsAsync(
                        cancellationToken
                    );
                    foreach (ProductManifest m in available)
                        latestByProduct[m.ProductId] = (m, feed.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load products from feed {FeedId}", feed.Id);
                }
            }

            List<UpdateItemViewModel> updateItems = [];
            foreach (InstalledProduct product in installed)
            {
                if (
                    latestByProduct.TryGetValue(
                        product.ProductId,
                        out (ProductManifest Manifest, string FeedId) latest
                    ) && VersionComparer.IsNewer(latest.Manifest.Version, product.Version)
                )
                {
                    updateItems.Add(
                        new UpdateItemViewModel
                        {
                            ProductId = product.ProductId,
                            Title = product.Title,
                            CurrentVersion = product.Version,
                            AvailableVersion = latest.Manifest.Version,
                            ReleaseNotes = latest.Manifest.ReleaseNotes ?? string.Empty,
                            InstalledPath = product.InstalledPath,
                            FeedId = latest.FeedId,
                        }
                    );
                }
            }

            Updates = new ObservableCollection<UpdateItemViewModel>(updateItems);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorMessage =
                LocalizationManager.GetString("Error_ServerConnectionFailed") + ": " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        try
        {
            IsUpdatingAll = true;
            List<UpdateItemViewModel> snapshot = [.. Updates];
            foreach (UpdateItemViewModel update in snapshot)
            {
                await UpdateSingleAsync(update);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = LocalizationManager.GetString("Error_UpdateFailed") + ": " + ex.Message;
        }
        finally
        {
            IsUpdatingAll = false;
        }
    }

    [RelayCommand]
    private async Task UpdateSingleAsync(UpdateItemViewModel update)
    {
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken cancellationToken = cts.Token;

            InstalledProduct? installed = await _productRepository.GetByIdAsync(
                update.ProductId,
                cancellationToken: cancellationToken
            );
            IRegistryClient client = _feedRegistry.GetClient(update.FeedId);
            ProductManifest? manifest = await client.GetProductManifestAsync(
                update.ProductId,
                cancellationToken
            );

            if (installed is null || manifest is null)
                return;

            bool needsAdmin =
                ElevationHelper.PathRequiresAdmin(installed.InstalledPath)
                && !ElevationHelper.IsRunningAsAdmin();

            if (needsAdmin)
            {
                update.IsUpdating = true;
                update.UpdateStatusMessage = LocalizationManager.GetString("Install_Installing");

                bool success = await Task.Run(
                    () =>
                        ElevationHelper.RunElevatedUpdate(
                            update.ProductId,
                            installed.InstalledPath,
                            update.FeedId
                        ),
                    cancellationToken
                );

                // Reload repository from disk (elevated process modified it)
                await _productRepository.ReloadAsync(cancellationToken);

                update.IsUpdating = false;

                if (!success)
                {
                    ErrorMessage = LocalizationManager
                        .GetString("Error_UpdateProductFailed")
                        .Replace("{0}", update.Title);
                    return;
                }

                Updates.Remove(update);
                return;
            }

            // Non-elevated path: use UpdateAsync with progress
            update.IsUpdating = true;
            update.UpdatePercentage = 0;
            InstallOptions options = new InstallOptions(
                TargetPath: installed.InstalledPath,
                FeedId: update.FeedId
            );
            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                update.UpdatePercentage = p.Percentage;
                update.UpdateStatusMessage = p.Message;
            });

            InstallResult updateResult = await _coordinator.UpdateWithIsolationAsync(
                installed,
                manifest,
                options,
                progress,
                cancellationToken
            );

            update.IsUpdating = false;

            if (!updateResult.Success)
            {
                ErrorMessage =
                    LocalizationManager
                        .GetString("Error_UpdateProductFailed")
                        .Replace("{0}", update.Title)
                    + ": "
                    + (updateResult.ErrorMessage ?? string.Empty);
                return;
            }

            Updates.Remove(update);
        }
        catch (Exception ex)
        {
            update.IsUpdating = false;
            ErrorMessage =
                LocalizationManager
                    .GetString("Error_UpdateProductFailed")
                    .Replace("{0}", update.Title)
                + ": "
                + ex.Message;
        }
    }
}
