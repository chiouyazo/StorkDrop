using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
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
    private readonly InstallationTracker _tracker;
    private readonly ILogger<UpdatesViewModel> _logger;

    public UpdatesViewModel(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        InstallationCoordinator coordinator,
        InstallationTracker tracker,
        ILogger<UpdatesViewModel> logger
    )
    {
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _coordinator = coordinator;
        _tracker = tracker;
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

            List<UpdateItemViewModel> updateItems = [];
            foreach (InstalledProduct product in installed)
            {
                if (string.IsNullOrEmpty(product.FeedId))
                    continue;

                try
                {
                    IRegistryClient client = _feedRegistry.GetClient(product.FeedId);
                    ProductManifest? latest = await client.GetProductManifestAsync(
                        product.ProductId,
                        cancellationToken
                    );

                    if (
                        latest is not null
                        && VersionComparer.IsNewer(latest.Version, product.Version)
                    )
                    {
                        updateItems.Add(
                            new UpdateItemViewModel
                            {
                                ProductId = product.ProductId,
                                Title = product.Title,
                                CurrentVersion = product.Version,
                                AvailableVersion = latest.Version,
                                ReleaseNotes = latest.ReleaseNotes ?? string.Empty,
                                InstalledPath = product.InstalledPath,
                                FeedId = product.FeedId,
                                InstanceId = product.InstanceId,
                            }
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to check updates for {ProductId} on feed {FeedId}",
                        product.ProductId,
                        product.FeedId
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
                update.InstanceId,
                cancellationToken
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

                TrackedInstallation elevatedTracked = _tracker.StartInstallation(
                    update.ProductId,
                    $"Updating (elevated): {update.Title} -> v{update.AvailableVersion}"
                );
                elevatedTracked.AddLog(
                    $"Updating {update.Title} v{update.CurrentVersion} -> v{update.AvailableVersion} (elevated)"
                );

                bool success = await Task.Run(
                    () =>
                        ElevationHelper.RunElevatedUpdate(
                            update.ProductId,
                            installed.InstalledPath,
                            update.FeedId,
                            installed.InstanceId
                        ),
                    cancellationToken
                );

                await _productRepository.ReloadAsync(cancellationToken);

                update.IsUpdating = false;

                if (!success)
                {
                    elevatedTracked.Complete(false, "Elevated update failed");
                    _tracker.NotifyChanged();
                    ErrorMessage = LocalizationManager
                        .GetString("Error_UpdateProductFailed")
                        .Replace("{0}", update.Title);
                    return;
                }

                elevatedTracked.Complete(true);
                _tracker.NotifyChanged();
                Updates.Remove(update);
                return;
            }

            // Non-elevated path: use UpdateAsync with progress
            update.IsUpdating = true;
            update.UpdatePercentage = 0;

            TrackedInstallation tracked = _tracker.StartInstallation(
                update.ProductId,
                $"Updating: {update.Title} -> v{update.AvailableVersion}"
            );
            tracked.AddLog(
                $"Updating {update.Title} from v{update.CurrentVersion} to v{update.AvailableVersion}"
            );

            InstallOptions options = new InstallOptions(
                TargetPath: installed.InstalledPath,
                InstanceId: installed.InstanceId,
                FeedId: update.FeedId
            );
            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                update.UpdatePercentage = p.Percentage;
                update.UpdateStatusMessage = p.Message;
                tracked.Percentage = p.Percentage;
                tracked.StatusMessage = p.Message;
                if (!string.IsNullOrEmpty(p.Message))
                    tracked.AddLog(p.Message);
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
                tracked.Complete(false, updateResult.ErrorMessage);
                _tracker.NotifyChanged();
                ErrorMessage =
                    LocalizationManager
                        .GetString("Error_UpdateProductFailed")
                        .Replace("{0}", update.Title)
                    + ": "
                    + (updateResult.ErrorMessage ?? string.Empty);
                return;
            }

            tracked.Complete(true);
            _tracker.NotifyChanged();
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
