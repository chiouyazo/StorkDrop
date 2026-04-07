using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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

public partial class InstalledViewModel : ObservableObject
{
    private readonly IProductRepository _productRepository;
    private readonly IFeedRegistry _feedRegistry;
    private readonly InstallationCoordinator _coordinator;
    private readonly UninstallService _uninstallService;
    private readonly InstallationTracker _tracker;
    private readonly INotificationService _notificationService;
    private readonly DialogService _dialogService;
    private readonly ILogger<InstalledViewModel> _logger;

    public InstalledViewModel(
        IProductRepository productRepository,
        IFeedRegistry feedRegistry,
        InstallationCoordinator coordinator,
        UninstallService uninstallService,
        InstallationTracker tracker,
        INotificationService notificationService,
        DialogService dialogService,
        ILogger<InstalledViewModel> logger
    )
    {
        _productRepository = productRepository;
        _feedRegistry = feedRegistry;
        _coordinator = coordinator;
        _uninstallService = uninstallService;
        _tracker = tracker;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _logger = logger;
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
            List<InstalledProductViewModel> productVms = [];
            foreach (InstalledProduct p in installed)
            {
                bool hasPlugins = false;
                string manifestPath = Path.Combine(p.InstalledPath, ".stork", "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        string json = File.ReadAllText(manifestPath);
                        ProductManifest? manifest = JsonSerializer.Deserialize<ProductManifest>(
                            json,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );
                        hasPlugins = manifest?.Plugins is { Length: > 0 };
                    }
                    catch { }
                }

                if (!hasPlugins && p.FeedId is not null)
                {
                    try
                    {
                        IRegistryClient client = _feedRegistry.GetClient(p.FeedId);
                        ProductManifest? feedManifest = await client.GetProductManifestAsync(
                            p.ProductId,
                            cancellationToken
                        );
                        hasPlugins = feedManifest?.Plugins is { Length: > 0 };
                    }
                    catch { }
                }

                productVms.Add(
                    new InstalledProductViewModel
                    {
                        ProductId = p.ProductId,
                        Title = p.Title,
                        Version = p.Version,
                        InstalledPath = p.InstalledPath,
                        InstalledDate = p.InstalledDate,
                        HasPlugins = hasPlugins,
                        InstallType = p.InstallType ?? InstallType.Plugin,
                    }
                );
            }
            Products = new ObservableCollection<InstalledProductViewModel>(productVms);
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

                // Reload repository from disk (elevated process modified it)
                await _productRepository.ReloadAsync();
                Products.Remove(product);
                OnPropertyChanged(nameof(HasProducts));
                OnPropertyChanged(nameof(HasNoProducts));
                _dialogService.ShowInfo(
                    LocalizationManager
                        .GetString("Info_UninstallSuccess")
                        .Replace("{0}", product.Title)
                );
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
                try
                {
                    await _coordinator.UninstallWithIsolationAsync(installed, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogInformation(
                        "Uninstall of {ProductId} failed with access denied, retrying elevated",
                        product.ProductId
                    );
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
                    await _productRepository.ReloadAsync();
                }

                Products.Remove(product);
                OnPropertyChanged(nameof(HasProducts));
                OnPropertyChanged(nameof(HasNoProducts));

                string message = _uninstallService.RequiresReboot
                    ? LocalizationManager
                        .GetString("Info_UninstallNeedsReboot")
                        .Replace("{0}", product.Title)
                    : LocalizationManager
                        .GetString("Info_UninstallSuccess")
                        .Replace("{0}", product.Title);
                _dialogService.ShowInfo(message);
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

    [RelayCommand]
    private async Task ReExecutePluginsAsync(InstalledProductViewModel product)
    {
        try
        {
            InstalledProduct? installed = await _productRepository.GetByIdAsync(product.ProductId);
            if (installed is null)
                return;

            TrackedInstallation tracked = _tracker.StartInstallation(
                product.ProductId,
                $"Running actions: {product.Title}"
            );
            tracked.AddLog($"Re-executing plugin actions for {product.Title} v{product.Version}");

            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                tracked.Percentage = p.Percentage;
                tracked.StatusMessage = p.Message;
                if (!string.IsNullOrEmpty(p.Message))
                    tracked.AddLog(p.Message);
            });

            InstallResult result = await _coordinator.ReExecutePluginsWithIsolationAsync(
                installed,
                new ReExecuteOptions(),
                progress,
                tracked.Cts.Token
            );

            if (result.Success)
            {
                tracked.Complete(true);
                _tracker.NotifyChanged();
                try
                {
                    _notificationService.ShowSuccess(
                        LocalizationManager
                            .GetString("ReExecute_Success")
                            .Replace("{0}", product.Title),
                        string.Empty
                    );
                }
                catch { }
            }
            else
            {
                tracked.Complete(false, result.ErrorMessage);
                _tracker.NotifyChanged();
                try
                {
                    _notificationService.ShowError(
                        LocalizationManager
                            .GetString("ReExecute_Failed")
                            .Replace("{0}", product.Title),
                        result.ErrorMessage ?? string.Empty
                    );
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-execute plugins for {ProductId}", product.ProductId);
        }
    }
}
