using System.Collections.ObjectModel;
using System.IO;
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
    private readonly IFeedLockService _feedLock;
    private readonly ILogger<UpdatesViewModel> _logger;

    public UpdatesViewModel(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        InstallationCoordinator coordinator,
        InstallationTracker tracker,
        IFeedLockService feedLock,
        ILogger<UpdatesViewModel> logger
    )
    {
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _coordinator = coordinator;
        _tracker = tracker;
        _feedLock = feedLock;
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

            List<UpdateItemViewModel> updateItems = await Task.Run(async () =>
            {
                IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync(
                    cancellationToken
                );

                List<UpdateItemViewModel> items = [];
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
                            items.Add(
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
                return items;
            });

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
            // Prompt at most once per locked feed for the whole batch.
            FeedUnlockScope scope = _feedLock.CreateScope();
            List<UpdateItemViewModel> snapshot = [.. Updates];
            foreach (UpdateItemViewModel update in snapshot)
            {
                if (
                    !await _feedLock.EnsureAuthorizedAsync(
                        update.FeedId,
                        LocalizationManager.GetString("FeedLock_Op_Update"),
                        scope
                    )
                )
                    break; // user cancelled the unlock -> stop the batch

                await UpdateOneAsync(update);
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
        if (
            !await _feedLock.EnsureAuthorizedAsync(
                update.FeedId,
                LocalizationManager.GetString("FeedLock_Op_Update")
            )
        )
            return;

        await UpdateOneAsync(update);
    }

    private async Task UpdateOneAsync(UpdateItemViewModel update)
    {
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken cancellationToken = cts.Token;

            var fetchResult = await Task.Run(async () =>
            {
                InstalledProduct? inst = await _productRepository.GetByIdAsync(
                    update.ProductId,
                    update.InstanceId,
                    cancellationToken
                );
                IRegistryClient cl = _feedRegistry.GetClient(update.FeedId);
                ProductManifest? man = await cl.GetProductManifestAsync(
                    update.ProductId,
                    cancellationToken
                );
                return (inst, man);
            });

            InstalledProduct? installed = fetchResult.inst;
            ProductManifest? manifest = fetchResult.man;

            if (installed is null || manifest is null)
                return;

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

            InstallResult updateResult = await Task.Run(() =>
                _coordinator.UpdateWithIsolationAsync(
                    installed,
                    manifest,
                    options,
                    progress,
                    cancellationToken
                )
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

            string pluginsDir = Path.GetFullPath(StorkPaths.PluginsDirectory);
            string resolvedTarget = Path.GetFullPath(installed.InstalledPath);
            if (resolvedTarget.StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBoxResult restartResult = System.Windows.MessageBox.Show(
                    LocalizationManager
                        .GetString("Restart_PluginInstalled")
                        .Replace("{0}", update.Title),
                    "StorkDrop",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question
                );
                if (restartResult == System.Windows.MessageBoxResult.Yes)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
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
