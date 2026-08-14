using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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
    private readonly IFeedLockService _feedLock;
    private readonly IIntegrityService _integrity;
    private readonly ILogger<InstalledViewModel> _logger;

    public InstalledViewModel(
        IProductRepository productRepository,
        IFeedRegistry feedRegistry,
        InstallationCoordinator coordinator,
        UninstallService uninstallService,
        InstallationTracker tracker,
        INotificationService notificationService,
        DialogService dialogService,
        IFeedLockService feedLock,
        IIntegrityService integrity,
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
        _feedLock = feedLock;
        _integrity = integrity;
        _logger = logger;
    }

    [ObservableProperty]
    private ObservableCollection<InstalledProductViewModel> _products =
        new ObservableCollection<InstalledProductViewModel>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    private List<InstalledProductViewModel> _allProducts = [];

    public event Action<string>? NavigateToProductDetail;

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

            bool hasFileHandlers;
            try
            {
                hasFileHandlers = StorkDrop
                    .App.App.Services.GetServices<IStorkDropPlugin>()
                    .Any(plugin => plugin is IFileTypeHandler);
            }
            catch
            {
                hasFileHandlers = false;
            }

            List<InstalledProductViewModel> productVms = await Task.Run(async () =>
            {
                IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync(
                    cancellationToken
                );
                List<InstalledProductViewModel> vms = [];
                foreach (InstalledProduct p in installed)
                {
                    bool hasPlugins = false;
                    string manifestPath = StorkPaths.ProductManifestFile(
                        p.ProductId,
                        p.InstanceUniqueId ?? string.Empty
                    );
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

                    bool hasFileHandlerData = false;
                    string storkFilesDir = StorkPaths.ProductHandledFilesDir(
                        p.ProductId,
                        p.InstanceUniqueId ?? string.Empty
                    );
                    try
                    {
                        if (
                            Directory.Exists(storkFilesDir)
                            && Directory.GetFiles(storkFilesDir).Length > 0
                        )
                            hasFileHandlerData = true;
                    }
                    catch { }

                    if (!hasFileHandlerData)
                    {
                        if (hasFileHandlers && p.FeedId is not null)
                            hasFileHandlerData = true;
                    }

                    vms.Add(
                        new InstalledProductViewModel
                        {
                            ProductId = p.ProductId,
                            InstanceId = p.InstanceId,
                            Title = p.Title,
                            Version = p.Version,
                            InstalledPath = p.InstalledPath,
                            InstalledDate = p.InstalledDate,
                            HasPlugins = hasPlugins,
                            HasFileHandlerData = hasFileHandlerData,
                            InstallType = p.InstallType ?? InstallType.Plugin,
                            FeedId = p.FeedId,
                            BadgeText = p.BadgeText,
                            BadgeColor = p.BadgeColor,
                            InstanceUniqueId = p.InstanceUniqueId ?? string.Empty,
                        }
                    );
                }
                return vms;
            });

            _allProducts = productVms;
            ApplySearchFilter();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasProducts));
            OnPropertyChanged(nameof(HasNoProducts));
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            Products = new ObservableCollection<InstalledProductViewModel>(_allProducts);
        }
        else
        {
            List<InstalledProductViewModel> filtered = _allProducts
                .Where(p =>
                    p.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || p.ProductId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || p.InstanceId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            Products = new ObservableCollection<InstalledProductViewModel>(filtered);
        }
        OnPropertyChanged(nameof(HasProducts));
        OnPropertyChanged(nameof(HasNoProducts));
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

        if (
            !await _feedLock.EnsureAuthorizedAsync(
                product.FeedId,
                LocalizationManager.GetString("FeedLock_Op_Uninstall")
            )
        )
            return;

        TrackedInstallation? tracked = null;
        try
        {
            if (needsAdmin)
            {
                bool elevated = await Task.Run(() =>
                    ElevationHelper.RunElevatedUninstall(product.ProductId, product.InstanceId)
                );

                if (!elevated)
                {
                    _dialogService.ShowError(
                        LocalizationManager.GetString("Error_AdminDenied_Uninstall")
                    );
                    return;
                }

                await Task.Run(() => _productRepository.ReloadAsync());
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

            InstalledProduct? installed = await Task.Run(() =>
                _productRepository.GetByIdAsync(
                    product.ProductId,
                    product.InstanceId,
                    cancellationToken
                )
            );
            if (installed is not null)
            {
                tracked = _tracker.StartInstallation(
                    product.ProductId,
                    $"Uninstalling: {product.DisplayName}"
                );
                using var logScope = Serilog.Context.LogContext.PushProperty(
                    "InstallId",
                    tracked.Id
                );
                tracked.AddLog($"Uninstalling {product.Title} v{product.Version}");

                Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
                {
                    tracked.Percentage = p.Percentage;
                    tracked.StatusMessage = p.Message;
                    if (!string.IsNullOrEmpty(p.Message))
                        tracked.AddLog(p.Message);
                });

                try
                {
                    await Task.Run(() =>
                        _coordinator.UninstallWithIsolationAsync(
                            installed,
                            progress,
                            cancellationToken
                        )
                    );
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogInformation(
                        "Uninstall of {ProductId} failed with access denied, retrying elevated",
                        product.ProductId
                    );
                    bool elevated = await Task.Run(() =>
                        ElevationHelper.RunElevatedUninstall(product.ProductId, product.InstanceId)
                    );
                    if (!elevated)
                    {
                        tracked.Complete(false, "Administrator rights denied");
                        _tracker.NotifyChanged();
                        _dialogService.ShowError(
                            LocalizationManager.GetString("Error_AdminDenied_Uninstall")
                        );
                        return;
                    }
                    await Task.Run(() => _productRepository.ReloadAsync());
                }

                tracked.Complete(true);
                _tracker.NotifyChanged();

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
            tracked?.Complete(false, $"File locked: {ex.FileName}");
            _tracker.NotifyChanged();
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
            tracked?.Complete(false, ex.Message);
            _tracker.NotifyChanged();
            _dialogService.ShowError(
                LocalizationManager.GetString("Error_UninstallFailed") + ": " + ex.Message
            );
        }
    }

    [RelayCommand]
    private void SwitchChannel(InstalledProductViewModel product)
    {
        NavigateToProductDetail?.Invoke(product.ProductId);
    }

    /// <summary>
    /// Verifies the product's installed files against their install-time hashes and, if any are
    /// corrupted or missing, offers a dialog to selectively repair them by re-downloading.
    /// </summary>
    [RelayCommand]
    private async Task VerifyFilesAsync(InstalledProductViewModel product)
    {
        try
        {
            InstalledProduct? installed = await Task.Run(() =>
                _productRepository.GetByIdAsync(product.ProductId, product.InstanceId)
            );
            if (installed is null)
                return;

            TrackedInstallation tracked = _tracker.StartInstallation(
                product.ProductId,
                $"Verifying: {product.Title}"
            );
            using var verifyScope = Serilog.Context.LogContext.PushProperty(
                "InstallId",
                tracked.Id
            );
            tracked.AddLog($"Verifying files for {product.Title} v{product.Version}");
            Progress<int> progress = new Progress<int>(p =>
            {
                tracked.Percentage = p;
                tracked.StatusMessage = $"Verifying files... {p}%";
            });

            IntegrityReport report = await Task.Run(() =>
                _integrity.VerifyAsync(installed, progress, tracked.Cts.Token)
            );
            tracked.Complete(true);
            _tracker.NotifyChanged();

            if (report.Files.Count == 0)
            {
                _dialogService.ShowInfo(LocalizationManager.GetString("Integrity_NoFiles"));
                return;
            }

            if (!report.HasProblems)
            {
                string okMessage =
                    report.UnverifiableCount > 0
                        ? LocalizationManager
                            .GetString("Integrity_OkWithUnverifiable")
                            .Replace("{0}", report.OkCount.ToString())
                            .Replace("{1}", report.UnverifiableCount.ToString())
                        : LocalizationManager
                            .GetString("Integrity_AllOk")
                            .Replace("{0}", report.OkCount.ToString());
                _dialogService.ShowInfo(okMessage);
                return;
            }

            IReadOnlyList<string>? selected = System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    Views.IntegrityDialog dialog = new Views.IntegrityDialog(
                        product.Title,
                        report.Problems
                    )
                    {
                        Owner = System.Windows.Application.Current.MainWindow,
                    };
                    return dialog.ShowDialog() == true ? dialog.SelectedPaths : null;
                }
            );

            if (selected is null || selected.Count == 0)
                return;

            if (
                !await _feedLock.EnsureAuthorizedAsync(
                    product.FeedId,
                    LocalizationManager.GetString("FeedLock_Op_Repair")
                )
            )
                return;

            TrackedInstallation repairTracked = _tracker.StartInstallation(
                product.ProductId,
                $"Repairing: {product.Title}"
            );
            using var repairScope = Serilog.Context.LogContext.PushProperty(
                "InstallId",
                repairTracked.Id
            );
            Progress<int> repairProgress = new Progress<int>(p =>
            {
                repairTracked.Percentage = p;
                repairTracked.StatusMessage = $"Repairing files... {p}%";
            });

            int repaired = await Task.Run(() =>
                _integrity.RepairAsync(installed, selected, repairProgress, repairTracked.Cts.Token)
            );
            repairTracked.Complete(true);
            _tracker.NotifyChanged();

            _dialogService.ShowInfo(
                LocalizationManager
                    .GetString("Integrity_Repaired")
                    .Replace("{0}", repaired.ToString())
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verify/repair failed for {ProductId}", product.ProductId);
            _dialogService.ShowError(
                LocalizationManager.GetString("Integrity_Failed") + ": " + ex.Message
            );
        }
    }

    [RelayCommand]
    private async Task ReExecutePluginsAsync(InstalledProductViewModel product)
    {
        try
        {
            InstalledProduct? installed = await Task.Run(() =>
                _productRepository.GetByIdAsync(product.ProductId, product.InstanceId)
            );
            if (installed is null)
                return;

            if (
                !await _feedLock.EnsureAuthorizedAsync(
                    product.FeedId,
                    LocalizationManager.GetString("FeedLock_Op_Action")
                )
            )
                return;

            TrackedInstallation tracked = _tracker.StartInstallation(
                product.ProductId,
                $"Running actions: {product.Title}"
            );
            using var logScope = Serilog.Context.LogContext.PushProperty("InstallId", tracked.Id);
            tracked.AddLog($"Re-executing plugin actions for {product.Title} v{product.Version}");

            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                tracked.Percentage = p.Percentage;
                tracked.StatusMessage = p.Message;
                if (!string.IsNullOrEmpty(p.Message))
                    tracked.AddLog(p.Message);
            });

            InstallResult result = await Task.Run(() =>
                _coordinator.ReExecutePluginsWithIsolationAsync(
                    installed,
                    new ReExecuteOptions { RunFileHandlers = product.HasFileHandlerData },
                    progress,
                    tracked.Cts.Token
                )
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
