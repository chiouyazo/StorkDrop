using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorkDrop.App.Localization;
using StorkDrop.App.Services;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;

namespace StorkDrop.App.ViewModels;

/// <summary>
/// View model for the installed products view, displaying installed products and handling uninstall.
/// </summary>
public partial class InstalledViewModel : ObservableObject
{
    private readonly IProductRepository _productRepository;
    private readonly IInstallationEngine _installationEngine;
    private readonly DialogService _dialogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstalledViewModel"/> class.
    /// </summary>
    /// <param name="productRepository">The repository for installed products.</param>
    /// <param name="installationEngine">The engine for installing and uninstalling products.</param>
    /// <param name="dialogService">The dialog service for user confirmations.</param>
    public InstalledViewModel(
        IProductRepository productRepository,
        IInstallationEngine installationEngine,
        DialogService dialogService
    )
    {
        _productRepository = productRepository;
        _installationEngine = installationEngine;
        _dialogService = dialogService;
    }

    [ObservableProperty]
    private ObservableCollection<InstalledProductViewModel> _products =
        new ObservableCollection<InstalledProductViewModel>();

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Gets a value indicating whether there are installed products.
    /// </summary>
    public bool HasProducts => Products.Count > 0;

    /// <summary>
    /// Gets a value indicating whether there are no installed products.
    /// </summary>
    public bool HasNoProducts => Products.Count == 0;

    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Loads all installed products from the repository.
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
            IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync(
                cancellationToken
            );
            Products = new ObservableCollection<InstalledProductViewModel>(
                installed.Select(p => new InstalledProductViewModel
                {
                    ProductId = p.ProductId,
                    Title = p.Title,
                    Version = p.Version,
                    InstalledPath = p.InstalledPath,
                    InstalledDate = p.InstalledDate,
                })
            );
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasProducts));
            OnPropertyChanged(nameof(HasNoProducts));
        }
    }

    /// <summary>
    /// Uninstalls the specified product after user confirmation.
    /// </summary>
    /// <param name="product">The installed product view model to uninstall.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task UninstallAsync(InstalledProductViewModel product)
    {
        bool needsAdmin =
            ElevationHelper.PathRequiresAdmin(product.InstalledPath)
            && !ElevationHelper.IsRunningAsAdmin();

        string confirmMessage = LocalizationManager
            .GetString("Confirm_Uninstall")
            .Replace("{0}", product.Title);
        if (needsAdmin)
            confirmMessage += "\n\n" + LocalizationManager.GetString("Uninstall_AdminHint");

        if (!_dialogService.ShowConfirmation(confirmMessage))
            return;

        try
        {
            if (needsAdmin)
            {
                bool elevated = await Task.Run(() =>
                    ElevationHelper.RunElevatedUninstall(product.ProductId)
                );

                if (!elevated)
                {
                    _dialogService.ShowError(
                        LocalizationManager.GetString("Error_AdminDenied_Uninstall")
                    );
                    return;
                }

                Products.Remove(product);
                OnPropertyChanged(nameof(HasProducts));
                OnPropertyChanged(nameof(HasNoProducts));
                return;
            }

            using CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken cancellationToken = cts.Token;

            InstalledProduct? installed = await _productRepository.GetByIdAsync(
                product.ProductId,
                cancellationToken
            );
            if (installed is not null)
            {
                await _installationEngine.UninstallAsync(installed, cancellationToken);
                Products.Remove(product);
                OnPropertyChanged(nameof(HasProducts));
                OnPropertyChanged(nameof(HasNoProducts));
                _dialogService.ShowInfo(
                    LocalizationManager
                        .GetString("Info_UninstallSuccess")
                        .Replace("{0}", product.Title)
                );
            }
        }
        catch (FileLockedException ex)
        {
            string message = string.IsNullOrEmpty(ex.ProcessNames)
                ? LocalizationManager.GetString("Error_FileInUse", ex.FileName, "?")
                : LocalizationManager.GetString(
                    "Error_UninstallFileLocked",
                    ex.FileName,
                    ex.ProcessNames
                );
            _dialogService.ShowError(message);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(
                LocalizationManager.GetString("Error_UninstallFailed") + ": " + ex.Message
            );
        }
    }
}
