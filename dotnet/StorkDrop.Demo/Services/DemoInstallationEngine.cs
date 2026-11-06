using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Data;
using StorkDrop.Demo.Plugins;
using StorkDrop.Demo.Sandbox;
using StorkDrop.Demo.Scenarios;
using StorkDrop.Installer;

namespace StorkDrop.Demo.Services;

/// <summary>
/// Demo engine: same steps, prompts, FailedStep values and messages as the real engine, but the work
/// is simulated and only touches files inside the demo sandbox.
/// </summary>
internal sealed class DemoInstallationEngine : IInstallationEngine
{
    private const int MaxDownloadAttempts = 4;
    private const int MaxLockedFilesPrompts = 5;

    private readonly IProductRepository _productRepository;
    private readonly IActivityLog _activityLog;
    private readonly IBackupService _backupService;
    private readonly IFileLockDetector _fileLockDetector;
    private readonly UninstallService _uninstallService;
    private readonly ILogger<DemoInstallationEngine> _logger;
    private readonly DemoInteractivePlugin _interactivePlugin = new DemoInteractivePlugin();

    public DemoInstallationEngine(
        IProductRepository productRepository,
        IActivityLog activityLog,
        IBackupService backupService,
        IFileLockDetector fileLockDetector,
        UninstallService uninstallService,
        ILogger<DemoInstallationEngine> logger
    )
    {
        _productRepository = productRepository;
        _activityLog = activityLog;
        _backupService = backupService;
        _fileLockDetector = fileLockDetector;
        _uninstallService = uninstallService;
        _logger = logger;
    }

    public InstallPathResolverCallback? OnResolveInstallPath { get; set; }
    public FileHandlerConfigCallback? OnFileHandlerConfigNeeded { get; set; }
    public FileHandlerConfigCallback? OnPluginConfigNeeded { get; set; }
    public ActionGroupConfigCallback? OnActionGroupConfigNeeded { get; set; }
    public LockedFilesCallback? OnLockedFilesDetected { get; set; }
    public Func<PluginPrompt, PluginPromptResult>? OnPrompt { get; set; }
    public Func<InstancePickRequest, InstalledProduct?>? OnPickInstalledInstance { get; set; }
    public Func<string, object[], string>? OnLocalize { get; set; }

    public IInteractiveStorkPlugin? CurrentInteractivePlugin => _interactivePlugin;

    private string Localize(string key, params object[] args) =>
        OnLocalize?.Invoke(key, args) ?? key;

    public Task<Dictionary<string, string>> LoadSavedPluginConfigAsync(
        string productId,
        string instanceUniqueId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new Dictionary<string, string>());

    public Task<IReadOnlyList<PluginActionGroup>> GetActionGroupsAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return Task.FromResult<IReadOnlyList<PluginActionGroup>>([]);

        IReadOnlyList<PluginActionDescription> descriptions =
            _interactivePlugin.GetActionDescriptions(new PluginEnvironment());

        List<PluginActionGroup> groups = descriptions
            .Select(desc => new PluginActionGroup
            {
                GroupId = $"{desc.Phase.ToString().ToLowerInvariant()}-demo-{desc.Title}",
                Title = desc.Title,
                Phase = desc.Phase,
                IsEnabled = desc.IsEnabled,
                Fields = desc.Fields,
                Descriptions = [desc],
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<PluginActionGroup>>(groups);
    }

    public Task<IReadOnlyList<PluginConfigField>> GetPluginConfigurationAsync(
        ProductManifest manifest,
        string? feedId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest.Plugins is not { Length: > 0 })
            return Task.FromResult<IReadOnlyList<PluginConfigField>>([]);

        return Task.FromResult(_interactivePlugin.GetConfigurationSchema(new PluginEnvironment()));
    }

    public async Task<InstallResult> InstallAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        string? hostVersionError = CheckMinHostVersion(manifest);
        if (hostVersionError is not null)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = hostVersionError,
                FailedStep = "HostVersion",
            };

        InstalledProduct? existing = await _productRepository.GetByIdAsync(
            manifest.ProductId,
            options.InstanceId,
            cancellationToken
        );
        InstallResult result = await InstallInternalAsync(
            manifest,
            options,
            progress,
            existing,
            cancellationToken
        );

        if (result.Success)
            await LogAsync(
                "Install",
                manifest.ProductId,
                $"Installed {manifest.Title} v{manifest.Version}"
            );

        return result;
    }

    public async Task UpdateAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Starting update of {ProductId} from {OldVersion} to {NewVersion}",
            installed.ProductId,
            installed.Version,
            newManifest.Version
        );

        string? hostVersionError = CheckMinHostVersion(newManifest);
        if (hostVersionError is not null)
            throw new InvalidOperationException(hostVersionError);

        string uniqueId = installed.InstanceUniqueId ?? string.Empty;
        List<TrackedFile> oldTracked =
            await DemoPackage.ReadFileManifestAsync(
                installed.ProductId,
                uniqueId,
                cancellationToken
            ) ?? [];
        List<string> trackedPaths = oldTracked
            .Select(f => Path.Combine(installed.InstalledPath, f.Path))
            .Where(File.Exists)
            .ToList();

        IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
            trackedPaths,
            cancellationToken
        );
        if (lockedFiles.Count > 0)
        {
            if (OnLockedFilesDetected is not null)
            {
                if (
                    !ResolveLockedFilesBeforeUpdate(
                        lockedFiles,
                        trackedPaths,
                        installed,
                        cancellationToken
                    )
                )
                    throw new OperationCanceledException(
                        $"Update of {installed.Title} cancelled: files in use were not released."
                    );
            }
            else
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Warning: {lockedFiles.Count} file(s) in use. They will be replaced on restart."
                    )
                );
            }
        }

        string? backupPath = null;
        if (options.CreateBackup && oldTracked.Count > 0)
        {
            progress.Report(
                new InstallProgress(
                    InstallStage.Downloading,
                    0,
                    $"Creating backup of {installed.Title} v{installed.Version}..."
                )
            );
            backupPath = await _backupService.CreateBackupAsync(
                installed.ProductId,
                installed.InstalledPath,
                oldTracked.Select(f => f.Path).ToList(),
                cancellationToken
            );
        }

        HashSet<string> preUpdateFiles = Directory.Exists(installed.InstalledPath)
            ? Directory
                .GetFiles(installed.InstalledPath, "*", SearchOption.AllDirectories)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            InstallResult result = await InstallInternalAsync(
                newManifest,
                options with
                {
                    RemovePreviousFiles = true,
                },
                progress,
                installed,
                cancellationToken
            );
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Installation failed.");

            InstalledProduct? updated = await _productRepository.GetByIdAsync(
                newManifest.ProductId,
                options.InstanceId,
                cancellationToken
            );
            if (updated is not null && backupPath is not null)
                await _productRepository.UpdateAsync(
                    updated with
                    {
                        BackupPath = backupPath,
                    },
                    cancellationToken
                );

            await LogAsync(
                "Update",
                newManifest.ProductId,
                $"Updated {newManifest.Title} from v{installed.Version} to v{newManifest.Version}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for {ProductId}", installed.ProductId);

            bool cancelled = cancellationToken.IsCancellationRequested;
            if (!cancelled && !ShouldRollBackAfterFailure(installed, newManifest, ex))
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Update failed but the new version was kept (not rolled back): {ex.Message}"
                    )
                );
                InstalledProduct baseRecord =
                    await _productRepository.GetByIdAsync(
                        newManifest.ProductId,
                        options.InstanceId,
                        CancellationToken.None
                    ) ?? installed;
                await _productRepository.UpdateAsync(
                    baseRecord with
                    {
                        Version = newManifest.Version,
                        FeedId = options.FeedId ?? baseRecord.FeedId,
                        BackupPath = backupPath,
                    },
                    CancellationToken.None
                );
                return;
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Update failed for {installed.ProductId}, restoring backup"
                )
            );

            if (backupPath is not null && Directory.Exists(installed.InstalledPath))
            {
                int removed = 0;
                foreach (
                    string current in Directory.GetFiles(
                        installed.InstalledPath,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    if (preUpdateFiles.Contains(current))
                        continue;

                    File.Delete(current);
                    removed++;
                }

                _logger.LogInformation(
                    "Rollback removed {Count} newly-added file(s) for {ProductId}",
                    removed,
                    installed.ProductId
                );
            }

            if (backupPath is not null)
            {
                _logger.LogInformation(
                    "Restoring backup for {ProductId} from {BackupPath}",
                    installed.ProductId,
                    backupPath
                );
                await _backupService.RestoreBackupAsync(
                    backupPath,
                    installed.InstalledPath,
                    CancellationToken.None
                );
            }

            await DemoPackage.WriteFileManifestAsync(
                installed.ProductId,
                uniqueId,
                oldTracked,
                CancellationToken.None
            );
            await _productRepository.UpdateAsync(installed, CancellationToken.None);
            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    $"Rollback complete: {installed.Title} v{installed.Version} was restored."
                )
            );

            if (cancelled)
                throw new OperationCanceledException(cancellationToken);
            throw;
        }
    }

    public Task UninstallAsync(
        InstalledProduct product,
        CancellationToken cancellationToken = default
    ) => _uninstallService.UninstallAsync(product, null, cancellationToken);

    public async Task<InstallResult> ReExecutePluginsAsync(
        InstalledProduct product,
        ReExecuteOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                5,
                $"Loading plugin data for {product.Title}..."
            )
        );
        await Task.Delay(300, cancellationToken);

        Dictionary<string, string> previousValues = new Dictionary<string, string>
        {
            ["target-database"] = "dev",
            ["schema-name"] = "dbo",
            ["timeout"] = "300",
        };

        ProductManifest pluginManifest = DemoProducts.Scenario(
            product.ProductId,
            product.Title,
            product.Version,
            string.Empty,
            withPlugin: true
        );
        IReadOnlyList<PluginActionGroup> groups = await GetActionGroupsAsync(
            pluginManifest,
            product.FeedId,
            cancellationToken
        );

        Dictionary<string, string>? configValues = null;
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                15,
                "Waiting for plugin configuration..."
            )
        );
        if (OnActionGroupConfigNeeded is not null && groups.Count > 0)
            configValues = OnActionGroupConfigNeeded(groups, previousValues);
        else if (OnPluginConfigNeeded is not null)
            configValues = OnPluginConfigNeeded(
                _interactivePlugin.GetConfigurationSchema(new PluginEnvironment()),
                previousValues
            );

        if (configValues is null)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Plugin configuration cancelled.",
            };

        foreach (
            (int percent, string message) in new[]
            {
                (25, "Running PreInstall..."),
                (35, "PreInstall: Connection to database verified."),
                (50, "Running PostInstall..."),
                (65, "PostInstall: Inserting default configuration..."),
                (85, "PostInstall: Verifying data integrity..."),
                (90, "Saving configuration..."),
            }
        )
        {
            progress.Report(new InstallProgress(InstallStage.RunningPlugins, percent, message));
            await Task.Delay(400, cancellationToken);
        }

        progress.Report(
            new InstallProgress(
                InstallStage.Verifying,
                100,
                $"Plugin actions for {product.Title} completed successfully."
            )
        );
        await LogAsync(
            "ReExecute",
            product.ProductId,
            $"Re-executed plugin actions for {product.Title} v{product.Version}"
        );
        return new InstallResult { Success = true };
    }

    public Task<InstallResult> SwitchChannelAsync(
        InstalledProduct installed,
        ProductManifest newChannelManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotImplementedException(
            "Channel switching will be implemented in a later phase."
        );

    private async Task<InstallResult> InstallInternalAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        InstalledProduct? existing,
        CancellationToken cancellationToken
    )
    {
        DemoBehavior behavior = DemoBehaviors.For(manifest.ProductId);
        bool fresh = existing is null;
        bool isUpdate = !fresh && options.RemovePreviousFiles;
        string uniqueId = existing?.InstanceUniqueId ?? InstanceIdHelper.GenerateUniqueId();
        string installPath = options.TargetPath;
        bool filesCopyStarted = false;
        int slowDelay = behavior.SlowUpdate && isUpdate ? 2500 : 0;

        try
        {
            InstallResult? downloadError = await DownloadAndExtractAsync(
                manifest,
                behavior,
                progress,
                cancellationToken
            );
            if (downloadError is not null)
                return downloadError;

            if (
                manifest.ProductId == DemoIds.NovaDashboard
                && OnFileHandlerConfigNeeded is not null
                && !options.SkipFileHandlers
            )
            {
                InstallResult? handlerError = await RunFileHandlerAsync(
                    progress,
                    cancellationToken
                );
                if (handlerError is not null)
                    return handlerError;
            }

            bool hasPlugins = manifest.Plugins is { Length: > 0 };
            if (hasPlugins && !behavior.SkipConfigDialog && options.PluginConfigValues is null)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.RunningPlugins,
                        50,
                        "Loading plugin configuration..."
                    )
                );
                IReadOnlyList<PluginActionGroup> groups = await GetActionGroupsAsync(
                    manifest,
                    options.FeedId,
                    cancellationToken
                );
                Dictionary<string, string>? pluginConfig = null;
                if (OnActionGroupConfigNeeded is not null && groups.Count > 0)
                    pluginConfig = OnActionGroupConfigNeeded(
                        groups,
                        new Dictionary<string, string>()
                    );
                else if (OnPluginConfigNeeded is not null)
                    pluginConfig = OnPluginConfigNeeded(
                        _interactivePlugin.GetConfigurationSchema(new PluginEnvironment()),
                        new Dictionary<string, string>()
                    );

                if (pluginConfig is null)
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = "Installation cancelled by user.",
                        FailedStep = "PluginConfig",
                    };
            }

            if (hasPlugins)
            {
                progress.Report(
                    new InstallProgress(InstallStage.RunningPlugins, 52, "Running PreInstall...")
                );
                await Task.Delay(400 + slowDelay, cancellationToken);

                if (behavior.PreInstallError is not null)
                {
                    _logger.LogError(
                        "Installation failed at step PreInstall: {Error}",
                        behavior.PreInstallError
                    );
                    progress.Report(
                        new InstallProgress(
                            InstallStage.Installing,
                            0,
                            $"Installation failed at step PreInstall: {behavior.PreInstallError}"
                        )
                    );
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = behavior.PreInstallError,
                        FailedStep = "PreInstall",
                    };
                }

                progress.Report(
                    new InstallProgress(
                        InstallStage.RunningPlugins,
                        58,
                        "PreInstall: All prerequisites met."
                    )
                );
            }

            if (isUpdate && existing is not null)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        62,
                        "Removing files of the previous version..."
                    )
                );
                await RemovePreviousFilesAsync(existing, cancellationToken);
                await Task.Delay(slowDelay, cancellationToken);
            }

            string resolved = options.TargetPath;
            if (OnResolveInstallPath is not null)
                resolved = OnResolveInstallPath(resolved, null) ?? resolved;

            installPath = DemoSandbox.Contain(resolved);
            if (!string.Equals(installPath, resolved, StringComparison.OrdinalIgnoreCase))
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        65,
                        $"Demo: install path redirected into the sandbox: {installPath}"
                    )
                );

            if (behavior.ElevationSlow)
            {
                InstallResult? elevationError = await RunElevatedAsync(progress, cancellationToken);
                if (elevationError is not null)
                    return elevationError;
            }

            filesCopyStarted = true;
            Progress<string> copyProgress = new Progress<string>(file =>
                progress.Report(
                    new InstallProgress(InstallStage.Installing, 75, $"Copying {file}...")
                )
            );
            List<TrackedFile> tracked = await DemoPackage.WriteAsync(
                manifest,
                installPath,
                copyProgress,
                TimeSpan.FromMilliseconds(200 + slowDelay),
                cancellationToken
            );

            if (hasPlugins)
            {
                progress.Report(
                    new InstallProgress(InstallStage.RunningPlugins, 85, "Running PostInstall...")
                );
                await Task.Delay(500 + slowDelay, cancellationToken);
                if (behavior.PostInstallError is not null)
                {
                    _logger.LogError(
                        "PostInstall plugin phase failed: {Error}",
                        behavior.PostInstallError
                    );
                    throw new InvalidOperationException(
                        $"PostInstall failed: {behavior.PostInstallError}"
                    );
                }

                progress.Report(
                    new InstallProgress(
                        InstallStage.RunningPlugins,
                        90,
                        "PostInstall: Configuration applied successfully."
                    )
                );
            }

            if (manifest.Shortcuts is { Length: > 0 })
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        92,
                        $"Creating {manifest.Shortcuts.Length} shortcut(s)... (demo: nothing is created)"
                    )
                );

            progress.Report(
                new InstallProgress(InstallStage.Installing, 94, "Saving file manifest...")
            );
            await DemoPackage.WriteFileManifestAsync(
                manifest.ProductId,
                uniqueId,
                tracked,
                cancellationToken
            );

            progress.Report(
                new InstallProgress(InstallStage.Verifying, 96, "Verifying installation...")
            );
            await Task.Delay(300, cancellationToken);

            await RegisterAsync(
                manifest,
                options,
                existing,
                installPath,
                uniqueId,
                cancellationToken
            );
            progress.Report(
                new InstallProgress(
                    InstallStage.Verifying,
                    100,
                    $"Installation of {manifest.Title} v{manifest.Version} completed successfully."
                )
            );
            return new InstallResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            if (fresh)
            {
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        "Installation cancelled. Cleaning up..."
                    )
                );
                CleanUpFailedFreshInstall(manifest, uniqueId, installPath, filesCopyStarted);
            }
            else
            {
                progress.Report(
                    new InstallProgress(InstallStage.Installing, 0, "Installation cancelled.")
                );
            }

            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Installation cancelled by user.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Installation failed for {ProductId}: {Error}",
                manifest.ProductId,
                ex.Message
            );

            if (fresh && !ShouldRemoveAfterInstallFailure(manifest, ex))
            {
                _logger.LogWarning(
                    "Install of {ProductId} failed but was kept on user request.",
                    manifest.ProductId
                );
                progress.Report(
                    new InstallProgress(
                        InstallStage.Installing,
                        0,
                        $"Installation failed but the product was kept: {ex.Message}"
                    )
                );
                await RegisterAsync(
                    manifest,
                    options,
                    existing,
                    installPath,
                    uniqueId,
                    CancellationToken.None
                );
                return new InstallResult { Success = true };
            }

            progress.Report(
                new InstallProgress(
                    InstallStage.Installing,
                    0,
                    fresh
                        ? $"Installation failed: {ex.Message}. Cleaning up..."
                        : $"Installation failed: {ex.Message}"
                )
            );
            if (fresh)
                CleanUpFailedFreshInstall(manifest, uniqueId, installPath, filesCopyStarted);

            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FailedStep = "Unknown",
                Exception = ex,
            };
        }
    }

    private async Task<InstallResult?> DownloadAndExtractAsync(
        ProductManifest manifest,
        DemoBehavior behavior,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        string description = $"{manifest.Title} v{manifest.Version}";
        long size = manifest.DownloadSizeBytes ?? 24_000_000;
        progress.Report(
            new InstallProgress(InstallStage.Downloading, 0, $"Downloading {description}...")
        );

        if (behavior.DiskFull || size > 1L << 40)
        {
            string root = Path.GetPathRoot(Path.GetTempPath()) ?? "C:\\";
            long available = new DriveInfo(root).AvailableFreeSpace;
            return StepFailure(
                progress,
                InstallStage.Downloading,
                "DiskSpace",
                new InsufficientDiskSpaceException(
                    root,
                    $"downloading {description}",
                    size + 16L * 1024 * 1024,
                    available
                )
            );
        }

        for (int attempt = 1; ; attempt++)
        {
            bool failThisAttempt =
                behavior.DownloadAlwaysFails || attempt <= behavior.TransientDownloadFailures;
            try
            {
                await StreamAsync(
                    description,
                    size,
                    failThisAttempt,
                    behavior,
                    progress,
                    cancellationToken
                );
                break;
            }
            catch (Exception ex)
                when (ex is HttpRequestException or IOException && attempt < MaxDownloadAttempts)
            {
                TimeSpan backoff = TimeSpan.FromSeconds(1 << (attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Download of {Package} failed (attempt {Attempt} of {Max}); retrying in {Delay}s",
                    description,
                    attempt,
                    MaxDownloadAttempts,
                    backoff.TotalSeconds
                );
                await Task.Delay(backoff, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                string cause = ex is HttpRequestException { StatusCode: HttpStatusCode status }
                    ? $"the server answered {(int)status} {status}"
                    : ex.Message;
                return StepFailure(
                    progress,
                    InstallStage.Downloading,
                    "Downloading",
                    new PackageDownloadException(
                        attempt > 1
                            ? $"Download of {description} failed after {attempt} attempts: {cause}"
                            : $"Download of {description} failed: {cause}",
                        ex
                    )
                );
            }
        }

        string actual = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description)))
            .ToLowerInvariant();
        string? expected = behavior.ChecksumMismatch
            ? new string('0', 64)
            : PackageHashVerifier.Normalize(manifest.ContentSha256);
        if (expected is null)
        {
            _logger.LogWarning(
                "The manifest of {Package} publishes no checksum; the download could NOT be verified",
                description
            );
        }
        else
        {
            try
            {
                PackageHashVerifier.EnsureMatches(expected, actual, description);
            }
            catch (PackageIntegrityException ex)
            {
                return StepFailure(progress, InstallStage.Downloading, "PackageVerification", ex);
            }
        }

        progress.Report(new InstallProgress(InstallStage.Downloading, 100, "Download complete."));
        progress.Report(new InstallProgress(InstallStage.Extracting, 0, "Extracting files..."));
        await Task.Delay(500, cancellationToken);
        progress.Report(new InstallProgress(InstallStage.Extracting, 100, "Extraction complete."));
        return null;
    }

    private static async Task StreamAsync(
        string description,
        long size,
        bool failThisAttempt,
        DemoBehavior behavior,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        for (int percent = 0; percent < 100; percent += 5)
        {
            if (failThisAttempt && percent >= 40)
            {
                if (behavior.DownloadAlwaysFails)
                    throw new HttpRequestException(
                        "Response status code does not indicate success: 503 (Service Unavailable).",
                        null,
                        HttpStatusCode.ServiceUnavailable
                    );

                throw new IOException(
                    "Unable to read data from the transport connection: The connection was forcibly closed by the remote host."
                );
            }

            long read = size * percent / 100;
            progress.Report(
                new InstallProgress(
                    InstallStage.Downloading,
                    percent,
                    $"Downloading {description}... {FormatHelper.FormatBytes(read)} of {FormatHelper.FormatBytes(size)}"
                )
            );
            await Task.Delay(180, cancellationToken);
        }
    }

    private async Task<InstallResult?> RunFileHandlerAsync(
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                48,
                "Plugin found 2 file(s): schema-update.sql, seed-data.sql"
            )
        );

        List<PluginConfigField> fileFields =
        [
            new PluginConfigField
            {
                Key = "target-db",
                Label = "Target Database",
                FieldType = PluginFieldType.Dropdown,
                Required = true,
                Options =
                [
                    new PluginOptionItem
                    {
                        Value = "production",
                        Label = "Production (db-prod-01)",
                    },
                    new PluginOptionItem { Value = "staging", Label = "Staging (db-staging-01)" },
                ],
            },
            new PluginConfigField
            {
                Key = "deploy-schema",
                Label = "Deploy schema-update.sql",
                FieldType = PluginFieldType.Checkbox,
                DefaultValue = "true",
            },
            new PluginConfigField
            {
                Key = "deploy-seed",
                Label = "Deploy seed-data.sql",
                FieldType = PluginFieldType.Checkbox,
                DefaultValue = "true",
            },
        ];

        Dictionary<string, string>? fileConfig = OnFileHandlerConfigNeeded!(
            fileFields,
            new Dictionary<string, string>()
        );
        if (fileConfig is null)
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Installation cancelled by user.",
                FailedStep = "FileHandlerConfig",
            };

        progress.Report(
            new InstallProgress(InstallStage.RunningPlugins, 50, "Processing SQL files...")
        );
        await Task.Delay(800, cancellationToken);
        progress.Report(
            new InstallProgress(
                InstallStage.RunningPlugins,
                52,
                "schema-update.sql: Deployed to "
                    + fileConfig.GetValueOrDefault("target-db", "production")
            )
        );
        await Task.Delay(300, cancellationToken);
        return null;
    }

    private async Task<InstallResult?> RunElevatedAsync(
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        progress.Report(
            new InstallProgress(InstallStage.Installing, 0, Localize("Elevation_Starting"))
        );
        await Task.Delay(1500, cancellationToken);
        string step = "Copying program files...";
        progress.Report(new InstallProgress(InstallStage.Installing, 60, step));
        await Task.Delay(1500, cancellationToken);

        if (OnPrompt is null)
            return null;

        PluginPromptResult response = OnPrompt(
            new PluginPrompt
            {
                Title = Localize("Elevation_TakingLong_Title"),
                Message = Localize("Elevation_TakingLong_Message", step),
                Options = [Localize("Elevation_WaitMore"), Localize("Elevation_Cancel")],
                DefaultOptionIndex = 0,
            }
        );
        if (response.ChosenIndex == 0)
            return null;

        return new InstallResult
        {
            Success = false,
            ErrorMessage =
                "The elevated step failed: the installation was cancelled while it was taking too long.",
            FailedStep = "ElevatedInstall",
        };
    }

    private async Task RemovePreviousFilesAsync(
        InstalledProduct existing,
        CancellationToken cancellationToken
    )
    {
        List<TrackedFile>? tracked = await DemoPackage.ReadFileManifestAsync(
            existing.ProductId,
            existing.InstanceUniqueId ?? string.Empty,
            cancellationToken
        );
        foreach (TrackedFile file in tracked ?? [])
        {
            string path = Path.Combine(existing.InstalledPath, file.Path);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private async Task RegisterAsync(
        ProductManifest manifest,
        InstallOptions options,
        InstalledProduct? existing,
        string installPath,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        InstalledProduct record = new InstalledProduct(
            manifest.ProductId,
            options.InstanceId,
            manifest.Title,
            manifest.Version,
            installPath,
            DateTime.UtcNow,
            options.FeedId ?? existing?.FeedId,
            BackupPath: existing?.BackupPath,
            InstallType: manifest.InstallType,
            BadgeText: manifest.BadgeText,
            BadgeColor: manifest.BadgeColor,
            InstanceUniqueId: uniqueId,
            Notes: existing?.Notes
        );
        await _productRepository.AddAsync(record, cancellationToken);
    }

    private void CleanUpFailedFreshInstall(
        ProductManifest manifest,
        string uniqueId,
        string installPath,
        bool removeInstallDir
    )
    {
        try
        {
            if (removeInstallDir && Directory.Exists(installPath))
                Directory.Delete(installPath, recursive: true);

            string manifestPath = StorkPaths.FileManifestPath(manifest.ProductId, uniqueId);
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Cleanup after failed install of {ProductId} failed",
                manifest.ProductId
            );
        }
    }

    private bool ShouldRollBackAfterFailure(
        InstalledProduct installed,
        ProductManifest newManifest,
        Exception ex
    )
    {
        if (OnPrompt is null)
            return true;

        PluginPromptResult choice = OnPrompt(
            new PluginPrompt
            {
                Title = Localize("Rollback_Title"),
                Message =
                    Localize(
                        "Rollback_Message",
                        installed.Title,
                        installed.Version,
                        newManifest.Version
                    )
                    + Environment.NewLine
                    + Environment.NewLine
                    + Localize("Rollback_KeepHint"),
                Detail = ex.Message,
                Options = [Localize("Rollback_Keep"), Localize("Rollback_Restore")],
                DefaultOptionIndex = 1,
            }
        );

        return choice.Cancelled || choice.ChosenIndex != 0;
    }

    private bool ShouldRemoveAfterInstallFailure(ProductManifest manifest, Exception ex)
    {
        if (OnPrompt is null)
            return true;

        PluginPromptResult choice = OnPrompt(
            new PluginPrompt
            {
                Title = Localize("Rollback_InstallTitle"),
                Message =
                    Localize("Rollback_InstallMessage", manifest.Title, manifest.Version)
                    + Environment.NewLine
                    + Environment.NewLine
                    + Localize("Rollback_InstallHint"),
                Detail = ex.Message,
                Options = [Localize("Rollback_KeepInstall"), Localize("Rollback_RemoveInstall")],
                DefaultOptionIndex = 1,
            }
        );

        return choice.Cancelled || choice.ChosenIndex != 0;
    }

    private bool ResolveLockedFilesBeforeUpdate(
        IReadOnlyList<LockedFileInfo> lockedFiles,
        IReadOnlyList<string> trackedPaths,
        InstalledProduct installed,
        CancellationToken cancellationToken
    )
    {
        for (int prompt = 0; prompt < MaxLockedFilesPrompts; prompt++)
        {
            LockedFilesAction action = OnLockedFilesDetected!(
                lockedFiles,
                _fileLockDetector,
                installed.InstalledPath
            );

            if (action == LockedFilesAction.RenameAndContinue)
                return true;

            if (action != LockedFilesAction.Retry)
                return false;

            lockedFiles = _fileLockDetector.GetLockedFiles(trackedPaths, cancellationToken);
            if (lockedFiles.Count == 0)
                return true;
        }

        return false;
    }

    private string? CheckMinHostVersion(ProductManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            return null;

        if (
            !PluginVersion.TryParse(manifest.MinHostVersion, out PluginVersion required)
            || !PluginVersion.TryParse(HostVersion.Current, out PluginVersion current)
            || current >= required
        )
            return null;

        string localized = Localize(
            "HostVersion_TooOld",
            manifest.Title,
            manifest.Version,
            manifest.MinHostVersion,
            HostVersion.Current
        );
        return localized == "HostVersion_TooOld"
            ? $"{manifest.Title} {manifest.Version} requires StorkDrop {manifest.MinHostVersion} (installed: {HostVersion.Current})."
            : localized;
    }

    private InstallResult StepFailure(
        IProgress<InstallProgress> progress,
        InstallStage stage,
        string failedStep,
        Exception exception
    )
    {
        _logger.LogError(
            exception,
            "Installation failed at step {Step}: {Error}",
            failedStep,
            exception.Message
        );
        progress.Report(new InstallProgress(stage, 0, exception.Message));
        return new InstallResult
        {
            Success = false,
            ErrorMessage = exception.Message,
            FailedStep = failedStep,
            Exception = exception,
        };
    }

    private Task LogAsync(string action, string productId, string details) =>
        _activityLog.LogAsync(
            new ActivityLogEntry(
                Guid.NewGuid().ToString(),
                DateTime.UtcNow,
                action,
                productId,
                details,
                true
            )
        );
}
