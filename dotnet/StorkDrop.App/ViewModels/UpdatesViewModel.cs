using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.Core.Interfaces;
using StorkDrop.Core.Models;
using StorkDrop.Core.Services;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the updates view, displaying available updates and handling update operations.
/// </summary>
public partial class UpdatesViewModel : ObservableObject
{
    private readonly IRegistryClient _registryClient;
    private readonly IProductRepository _productRepository;
    private readonly IInstallationEngine _installationEngine;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatesViewModel"/> class.
    /// </summary>
    /// <param name="registryClient">The registry client for fetching product information.</param>
    /// <param name="productRepository">The repository for installed products.</param>
    /// <param name="installationEngine">The engine for updating products.</param>
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
    private ObservableCollection<UpdateItemViewModel> _updates =
        new ObservableCollection<UpdateItemViewModel>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isUpdatingAll;

    private CancellationTokenSource? _cts;

    /// <summary>
    /// Loads the list of available updates.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

            List<UpdateItemViewModel> updateItems = new List<UpdateItemViewModel>();
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
                        }
                    );
                }
            }

            Updates = new ObservableCollection<UpdateItemViewModel>(updateItems);
        }
        catch (OperationCanceledException)
        {
            // Expected when reloading
        }
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

    /// <summary>
    /// Updates all available products.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        try
        {
            IsUpdatingAll = true;

            List<UpdateItemViewModel> snapshot = new List<UpdateItemViewModel>(Updates);

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

    /// <summary>
    /// Updates a single product.
    /// </summary>
    /// <param name="update">The update item to apply.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

            if (installed is not null && manifest is not null)
            {
                InstallOptions options = new InstallOptions(TargetPath: installed.InstalledPath);
                Progress<InstallProgress> progress = new Progress<InstallProgress>(_ => { });
                await _installationEngine.UpdateAsync(
                    installed,
                    manifest,
                    options,
                    progress,
                    cancellationToken
                );
                Updates.Remove(update);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage =
                LocalizationManager
                    .GetString("Error_UpdateProductFailed")
                    .Replace("{0}", update.Title)
                + ": "
                + ex.Message;
        }
    }
}
