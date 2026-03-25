using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;

namespace StorkDrop.App.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    private readonly IRegistryClient _registryClient;
    private readonly IProductRepository _productRepository;
    private readonly IInstallationEngine _installationEngine;

    public UpdatesViewModel(
        IRegistryClient registryClient,
        IProductRepository productRepository,
        IInstallationEngine installationEngine
    )
    {
        _registryClient = registryClient;
        _productRepository = productRepository;
        _installationEngine = installationEngine;
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
            IReadOnlyList<ProductManifest> available = await _registryClient.GetAllProductsAsync(
                cancellationToken
            );

            List<UpdateItemViewModel> updateItems = [];
            foreach (InstalledProduct product in installed)
            {
                ProductManifest? latest = available.FirstOrDefault(m =>
                    m.ProductId == product.ProductId
                );
                if (latest is not null && VersionComparer.IsNewer(latest.Version, product.Version))
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
                cancellationToken
            );
            ProductManifest? manifest = await _registryClient.GetProductManifestAsync(
                update.ProductId,
                cancellationToken
            );

            if (installed is null || manifest is null)
                return;

            // Check if UAC is needed
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
                            installed.InstalledPath
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
            InstallOptions options = new InstallOptions(TargetPath: installed.InstalledPath);
            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                update.UpdatePercentage = p.Percentage;
                update.UpdateStatusMessage = p.Message;
            });

            await _installationEngine.UpdateAsync(
                installed,
                manifest,
                options,
                progress,
                cancellationToken
            );

            update.IsUpdating = false;
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
