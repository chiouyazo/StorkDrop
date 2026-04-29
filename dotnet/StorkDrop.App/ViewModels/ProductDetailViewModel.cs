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

namespace StorkDrop.App.ViewModels;

public partial class ProductDetailViewModel : ObservableObject
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly InstallationCoordinator _coordinator;
    private readonly IProductRepository _productRepository;
    private readonly InstallationTracker _tracker;
    private readonly INotificationService _notificationService;
    private readonly PostProductResolver _postProductResolver;
    private readonly ILogger<ProductDetailViewModel> _logger;

    public ProductDetailViewModel(
        IFeedRegistry feedRegistry,
        InstallationCoordinator coordinator,
        IProductRepository productRepository,
        InstallationTracker tracker,
        INotificationService notificationService,
        PostProductResolver postProductResolver,
        ILogger<ProductDetailViewModel> logger
    )
    {
        _feedRegistry = feedRegistry;
        _coordinator = coordinator;
        _productRepository = productRepository;
        _tracker = tracker;
        _notificationService = notificationService;
        _postProductResolver = postProductResolver;
        _logger = logger;
    }

    public string FeedId { get; set; } = string.Empty;

    [ObservableProperty]
    private string _feedName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChannelInfo> _availableChannels =
        new ObservableCollection<ChannelInfo>();

    [ObservableProperty]
    private ChannelInfo? _selectedChannel;

    public bool HasMultipleChannels => AvailableChannels.Count > 1;

    private string? _installedFeedId;
    private bool _isInitialLoad;

    [ObservableProperty]
    private ProductManifest? _manifest;

    [ObservableProperty]
    private ObservableCollection<string> _availableVersions = new ObservableCollection<string>();

    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installedVersion = string.Empty;

    [ObservableProperty]
    private string _installButtonText = string.Empty;

    [ObservableProperty]
    private bool _isSelectedVersionInstalled;

    [ObservableProperty]
    private string _selectedVersionReleaseNotes = string.Empty;

    [ObservableProperty]
    private string _recommendedInstallPath = string.Empty;

    [ObservableProperty]
    private bool _hasPlugins;

    public string VersionDisplay => $"v{Manifest?.Version}";

    public string FeedDisplay => FeedName ?? string.Empty;

    public bool HasFeedDisplay => !string.IsNullOrEmpty(FeedName);

    public bool CanInstallSelectedVersion => !IsInstalling && !IsSelectedVersionInstalled;
    public bool CanReExecutePlugins => IsInstalled && HasPlugins && !IsInstalling;

    public event Action? GoBackRequested;

    partial void OnManifestChanged(ProductManifest? value) =>
        OnPropertyChanged(nameof(VersionDisplay));

    partial void OnAvailableChannelsChanged(ObservableCollection<ChannelInfo> value) =>
        OnPropertyChanged(nameof(HasMultipleChannels));

    partial void OnSelectedChannelChanged(ChannelInfo? value)
    {
        if (value is null || Manifest is null)
            return;

        if (_isInitialLoad)
            return;

        FeedId = value.FeedId;
        FeedName = value.FeedName;
        OnPropertyChanged(nameof(FeedDisplay));
        OnPropertyChanged(nameof(HasFeedDisplay));
        _ = ReloadManifestForChannelAsync(value, Manifest.ProductId);
    }

    private async Task ReloadManifestForChannelAsync(ChannelInfo channel, string productId)
    {
        try
        {
            IRegistryClient client = _feedRegistry.GetClient(channel.FeedId);
            ProductManifest? manifest = await client.GetProductManifestAsync(productId);
            if (manifest is null)
                return;

            Manifest = manifest;

            IReadOnlyList<string> versions = await client.GetAvailableVersionsAsync(productId);
            AvailableVersions = new ObservableCollection<string>(versions);
            SelectedVersion = manifest.Version;
            SelectedVersionReleaseNotes = manifest.ReleaseNotes ?? string.Empty;
            RecommendedInstallPath = manifest.RecommendedInstallPath ?? string.Empty;
            HasPlugins = manifest.Plugins is { Length: > 0 };
            UpdateInstallButtonText();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to reload manifest for channel {FeedId}",
                channel.FeedId
            );
        }
    }

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallSelectedVersion));
        OnPropertyChanged(nameof(CanReExecutePlugins));
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(CanReExecutePlugins));

    partial void OnHasPluginsChanged(bool value) => OnPropertyChanged(nameof(CanReExecutePlugins));

    partial void OnIsSelectedVersionInstalledChanged(bool value) =>
        OnPropertyChanged(nameof(CanInstallSelectedVersion));

    partial void OnSelectedVersionChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && Manifest is not null)
        {
            _ = LoadVersionReleaseNotesAsync(value);
            UpdateInstallButtonText();
        }
    }

    private void UpdateInstallButtonText()
    {
        bool isExecutable = Manifest?.InstallType == InstallType.Executable;

        if (IsInstalled && _installedFeedId is not null && FeedId != _installedFeedId)
        {
            IsSelectedVersionInstalled = false;
            InstallButtonText = LocalizationManager.GetString("Install_SwitchChannel");
        }
        else if (IsInstalled && SelectedVersion == InstalledVersion)
        {
            IsSelectedVersionInstalled = true;
            InstallButtonText = isExecutable
                ? LocalizationManager.GetString("Installed_Downloaded")
                : LocalizationManager.GetString("Install_Installed");
        }
        else if (IsInstalled)
        {
            IsSelectedVersionInstalled = false;
            InstallButtonText = LocalizationManager.GetString("Install_Update");
        }
        else
        {
            IsSelectedVersionInstalled = false;
            InstallButtonText = isExecutable
                ? LocalizationManager.GetString("Install_Download")
                : LocalizationManager.GetString("Install_Button");
        }
    }

    [RelayCommand]
    private async Task LoadAsync(string productId)
    {
        List<ChannelInfo> channels = new List<ChannelInfo>();
        IReadOnlyList<FeedInfo> feeds = _feedRegistry.GetFeeds();

        foreach (FeedInfo feed in feeds)
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                ProductManifest? manifest = await client.GetProductManifestAsync(productId);
                if (manifest is not null)
                {
                    ChannelInfo channel = new ChannelInfo(
                        FeedId: feed.Id,
                        FeedName: feed.Name,
                        BadgeText: manifest.BadgeText,
                        BadgeColor: manifest.BadgeColor,
                        LatestVersion: manifest.Version
                    );
                    channels.Add(channel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to check feed {FeedId} for product {ProductId}",
                    feed.Id,
                    productId
                );
            }
        }

        AvailableChannels = new ObservableCollection<ChannelInfo>(channels);

        IReadOnlyList<InstalledProduct> instances = await _productRepository.GetInstancesAsync(
            productId
        );
        InstalledProduct? installed = instances.FirstOrDefault();
        IsInstalled = installed is not null;
        InstalledVersion = installed?.Version ?? string.Empty;
        _installedFeedId = installed?.FeedId;

        ChannelInfo? selectedChannel =
            (
                installed?.FeedId is not null
                    ? channels.FirstOrDefault(c => c.FeedId == installed.FeedId)
                    : null
            )
            ?? (
                !string.IsNullOrEmpty(FeedId)
                    ? channels.FirstOrDefault(c => c.FeedId == FeedId)
                    : null
            )
            ?? channels.FirstOrDefault();

        if (selectedChannel is null)
            return;

        FeedId = selectedChannel.FeedId;
        FeedName = selectedChannel.FeedName;

        IRegistryClient selectedClient = _feedRegistry.GetClient(selectedChannel.FeedId);
        Manifest = await selectedClient.GetProductManifestAsync(productId);
        if (Manifest is null)
            return;

        IReadOnlyList<string> versions = await selectedClient.GetAvailableVersionsAsync(productId);
        AvailableVersions = new ObservableCollection<string>(versions);
        SelectedVersion = Manifest.Version;
        SelectedVersionReleaseNotes = Manifest.ReleaseNotes ?? string.Empty;
        RecommendedInstallPath = Manifest.RecommendedInstallPath ?? string.Empty;
        HasPlugins = Manifest.Plugins is { Length: > 0 };

        _isInitialLoad = true;
        SelectedChannel = selectedChannel;
        _isInitialLoad = false;
        UpdateInstallButtonText();
    }

    private async Task LoadVersionReleaseNotesAsync(string version)
    {
        if (Manifest is null)
            return;

        if (version == Manifest.Version)
        {
            SelectedVersionReleaseNotes = Manifest.ReleaseNotes ?? string.Empty;
            return;
        }

        try
        {
            IRegistryClient client = _feedRegistry.GetClient(FeedId);
            ProductManifest? versionManifest = await client.GetProductManifestAsync(
                Manifest.ProductId,
                version
            );
            SelectedVersionReleaseNotes = versionManifest?.ReleaseNotes ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load release notes for version {Version}", version);
            SelectedVersionReleaseNotes = string.Empty;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        GoBackRequested?.Invoke();
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (Manifest is null)
            return;

        try
        {
            IRegistryClient client = _feedRegistry.GetClient(FeedId);
            ProductManifest? manifest =
                SelectedVersion == Manifest.Version
                    ? Manifest
                    : await client.GetProductManifestAsync(Manifest.ProductId, SelectedVersion);

            if (manifest is null)
                return;

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
                    Manifest?.BadgeText
                );

                if (resolution.Available.Count > 0 || resolution.Warnings.Count > 0)
                {
                    Views.RequiredProductsDialog reqDialog = new(
                        manifest.Title,
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

            IsInstalling = true;

            TrackedInstallation tracked = _tracker.StartInstallation(
                manifest.ProductId,
                manifest.Title
            );
            tracked.AddLog($"Installing {manifest.Title} v{manifest.Version} to {targetPath}");

            InstallOptions options = new InstallOptions(TargetPath: targetPath, FeedId: FeedId);
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
                try
                {
                    _notificationService.ShowError(
                        $"Installation of {manifest.Title} failed",
                        installResult.ErrorMessage ?? string.Empty
                    );
                }
                catch { }
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
            catch { }

            IsInstalled = true;
            InstalledVersion = manifest.Version;
            UpdateInstallButtonText();

            await HandleOptionalPostProductsAsync(manifest);

            if (manifest.RecommendedInstallPath?.Contains("{StorkPath}") == true)
            {
                System.Windows.MessageBoxResult restartResult = System.Windows.MessageBox.Show(
                    LocalizationManager
                        .GetString("Restart_PluginInstalled")
                        .Replace("{0}", manifest.Title),
                    "StorkDrop",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question
                );
                if (restartResult == System.Windows.MessageBoxResult.Yes)
                {
                    string? exePath = System.Environment.ProcessPath;
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install product from detail view");
        }
        finally
        {
            IsInstalling = false;
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
            try
            {
                _notificationService.ShowError(
                    $"Installation of {manifest.Title} failed",
                    installResult.ErrorMessage ?? string.Empty
                );
            }
            catch { }
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
        catch { }
    }

    [RelayCommand]
    private async Task ReExecutePluginsAsync()
    {
        if (Manifest is null || !IsInstalled)
            return;

        try
        {
            IReadOnlyList<InstalledProduct> instances = await _productRepository.GetInstancesAsync(
                Manifest.ProductId
            );
            InstalledProduct? installed = instances.FirstOrDefault();
            if (installed is null)
                return;

            TrackedInstallation tracked = _tracker.StartInstallation(
                Manifest.ProductId,
                $"Running actions: {Manifest.Title}"
            );
            tracked.AddLog(
                $"Re-executing plugin actions for {Manifest.Title} v{installed.Version}"
            );

            Progress<InstallProgress> progress = new Progress<InstallProgress>(p =>
            {
                tracked.Percentage = p.Percentage;
                tracked.StatusMessage = p.Message;
                if (!string.IsNullOrEmpty(p.Message))
                    tracked.AddLog(p.Message);
            });

            IsInstalling = true;

            InstallResult result = await _coordinator.ReExecutePluginsWithIsolationAsync(
                installed,
                new ReExecuteOptions { RunFileHandlers = true },
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
                            .Replace("{0}", Manifest.Title),
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
                            .Replace("{0}", Manifest.Title),
                        result.ErrorMessage ?? string.Empty
                    );
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to re-execute plugins for {ProductId}",
                Manifest.ProductId
            );
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
