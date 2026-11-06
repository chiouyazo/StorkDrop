using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Handles product uninstallation including file manifest-based cleanup and plugin hooks.
/// </summary>
public sealed class UninstallService
{
    private const int MaxRetries = 3;
    private const int MaxUnloadCollections = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IProductRepository _productRepository;
    private readonly IActivityLog _activityLog;
    private readonly IFileLockDetector _fileLockDetector;
    private readonly EnvironmentVariableService _envVarService;
    private readonly DeferredFileOps _deferredFileOps;
    private readonly IFeedReportService _feedReportService;
    private readonly ILogger<UninstallService> _logger;

    /// <summary>
    /// Set to true after an uninstall deferred locked files for reboot deletion.
    /// </summary>
    public bool RequiresReboot { get; private set; }

    private bool _deferLockedFiles;

    internal string ProgramsFolder { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs"
        );

    /// <summary>
    /// Callback invoked when locked files are detected during uninstall.
    /// Allows the UI to show a dialog asking the user to close the locking processes.
    /// </summary>
    public LockedFilesCallback? OnLockedFilesDetected { get; set; }

    public UninstallService(
        IProductRepository productRepository,
        IActivityLog activityLog,
        IFileLockDetector fileLockDetector,
        EnvironmentVariableService envVarService,
        DeferredFileOps deferredFileOps,
        IFeedReportService feedReportService,
        ILogger<UninstallService> logger
    )
    {
        _productRepository = productRepository;
        _activityLog = activityLog;
        _fileLockDetector = fileLockDetector;
        _envVarService = envVarService;
        _deferredFileOps = deferredFileOps;
        _feedReportService = feedReportService;
        _logger = logger;
    }

    /// <summary>
    /// Uninstalls the specified product, removing tracked files and cleaning up empty directories.
    /// </summary>
    /// <param name="product">The installed product to uninstall.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UninstallAsync(
        InstalledProduct product,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Starting uninstall of {ProductId} from {InstalledPath}",
            product.ProductId,
            product.InstalledPath
        );

        RequiresReboot = false;
        _deferLockedFiles = false;

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 5, "Running pre-uninstall hooks...")
        );
        List<WeakReference> pluginLoadContexts = [];
        await RunUninstallPluginPhaseAsync(
            product,
            "PreUninstall",
            pluginLoadContexts,
            cancellationToken
        );

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 8, "Running post-uninstall hooks...")
        );
        await RunUninstallPluginPhaseAsync(
            product,
            "PostUninstall",
            pluginLoadContexts,
            cancellationToken
        );

        WaitForPluginUnload(pluginLoadContexts);

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 10, "Checking for locked files...")
        );
        List<string>? lockCheckManifest = await LoadFileManifestAsync(
            product.ProductId,
            product.InstanceUniqueId ?? string.Empty,
            cancellationToken
        );
        if (lockCheckManifest is { Count: > 0 } && Directory.Exists(product.InstalledPath))
        {
            List<string> trackedPaths = lockCheckManifest
                .Select(relativePath => Path.Combine(product.InstalledPath, relativePath))
                .Where(File.Exists)
                .ToList();

            _logger.LogInformation(
                "Checking {Count} tracked files for locks before uninstalling {ProductId}",
                trackedPaths.Count,
                product.ProductId
            );
            IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
                trackedPaths,
                cancellationToken
            );
            if (lockedFiles.Count > 0)
            {
                if (OnLockedFilesDetected is not null)
                {
                    ResolveLockedFilesChoice(
                        lockedFiles,
                        trackedPaths,
                        product.InstalledPath,
                        cancellationToken
                    );
                }
                else
                {
                    foreach (LockedFileInfo lockedFile in lockedFiles)
                    {
                        string processNames = string.Join(
                            ", ",
                            lockedFile.Processes.Select(p => p.ProcessName)
                        );
                        _logger.LogWarning(
                            "Locked file during uninstall: {File} (used by {Processes})",
                            lockedFile.FileName,
                            processNames
                        );
                    }
                }
            }
        }

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 15, "Removing environment variables...")
        );
        await RemoveEnvironmentVariablesAsync(
            product.ProductId,
            product.InstanceUniqueId ?? string.Empty,
            cancellationToken
        );

        if (Directory.Exists(product.InstalledPath))
        {
            List<TrackedFile>? entries = await LoadFileManifestEntriesAsync(
                product.ProductId,
                product.InstanceUniqueId ?? string.Empty,
                cancellationToken
            );
            bool shared = await IsSharedInstallLocationAsync(product, cancellationToken);

            List<string> filesToDelete;
            if (entries is null)
                filesToDelete = new List<string>();
            else if (shared)
                filesToDelete = entries
                    .Where(e => !string.IsNullOrEmpty(e.Sha256))
                    .Select(e => e.Path)
                    .ToList();
            else
                filesToDelete = entries.Select(e => e.Path).ToList();

            if (filesToDelete.Count > 0)
            {
                _logger.LogInformation(
                    "Deleting {Count} tracked files for {ProductId}",
                    filesToDelete.Count,
                    product.ProductId
                );

                int totalFiles = filesToDelete.Count;
                int deletedCount = 0;
                foreach (string relativePath in filesToDelete)
                {
                    string fullPath = Path.Combine(product.InstalledPath, relativePath);
                    if (File.Exists(fullPath))
                    {
                        await DeleteFileWithRetryAsync(fullPath, cancellationToken);
                    }

                    deletedCount++;
                    if (deletedCount % 50 == 0 || deletedCount == totalFiles)
                    {
                        int percentage = 20 + (int)(55.0 * deletedCount / totalFiles);
                        progress?.Report(
                            new InstallProgress(
                                InstallStage.Uninstalling,
                                percentage,
                                $"Deleting files... ({deletedCount}/{totalFiles})"
                            )
                        );
                    }
                }

                CleanupEmptyDirectories(product.InstalledPath);
            }
            else if (shared)
            {
                _logger.LogWarning(
                    "No verifiable tracked files for {ProductId} in shared install location '{Path}'; "
                        + "leaving the directory intact to avoid removing other products' or the host's files.",
                    product.ProductId,
                    product.InstalledPath
                );
            }
            else
            {
                _logger.LogWarning(
                    "No file manifest for {ProductId}; Remove any leftover files manually if needed.",
                    product.ProductId,
                    product.InstalledPath
                );
            }

            DeleteFileManifest(product.ProductId, product.InstanceUniqueId ?? string.Empty);
        }

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 80, "Removing shortcuts...")
        );
        RemoveShortcuts(product.Title, product.InstalledPath);

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 85, "Updating product registry...")
        );
        await _productRepository.RemoveAsync(
            product.ProductId,
            product.InstanceId,
            cancellationToken
        );

        ActivityLogEntry entry = new ActivityLogEntry(
            Id: Guid.NewGuid().ToString(),
            Timestamp: DateTime.UtcNow,
            Action: "Uninstall",
            ProductId: product.ProductId,
            Details: $"Uninstalled {product.Title} v{product.Version} from {product.InstalledPath}",
            Success: true
        );
        await _activityLog.LogAsync(entry, cancellationToken);

        progress?.Report(new InstallProgress(InstallStage.Uninstalling, 95, "Cleaning up..."));
        string storkDir = StorkPaths.ProductMetadataDir(
            product.ProductId,
            product.InstanceUniqueId ?? string.Empty
        );
        if (Directory.Exists(storkDir))
        {
            try
            {
                Directory.Delete(storkDir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete product metadata directory");
            }
        }

        progress?.Report(new InstallProgress(InstallStage.Uninstalling, 100, "Uninstall complete"));
        _logger.LogInformation(
            "Uninstall of {ProductId} v{Version} complete",
            product.ProductId,
            product.Version
        );

        await _feedReportService.NotifyFeedChangedAsync(product.FeedId, cancellationToken);
    }

    private static void WaitForPluginUnload(List<WeakReference> loadContexts)
    {
        for (
            int attempt = 0;
            attempt < MaxUnloadCollections && loadContexts.Exists(c => c.IsAlive);
            attempt++
        )
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private async Task RunUninstallPluginPhaseAsync(
        InstalledProduct product,
        string phase,
        List<WeakReference> loadContexts,
        CancellationToken cancellationToken
    )
    {
        string manifestPath = StorkPaths.ProductManifestFile(
            product.ProductId,
            product.InstanceUniqueId ?? string.Empty
        );
        if (!File.Exists(manifestPath))
            return;

        try
        {
            string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            ProductManifest? manifest =
                System.Text.Json.JsonSerializer.Deserialize<ProductManifest>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    }
                );

            if (manifest?.Plugins is not { Length: > 0 })
                return;

            string storkPluginsDir = StorkPaths.ProductMetadataDir(
                product.ProductId,
                product.InstanceUniqueId ?? string.Empty
            );

            Dictionary<string, string> savedConfigValues = new Dictionary<string, string>();
            try
            {
                string configPath = StorkPaths.InstancePluginConfigPath(
                    product.ProductId,
                    product.InstanceUniqueId ?? string.Empty
                );
                if (!File.Exists(configPath))
                    configPath = StorkPaths.LegacyPluginConfigPath(product.ProductId);

                if (File.Exists(configPath))
                {
                    string configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
                    savedConfigValues =
                        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                            configJson
                        )
                        ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load saved plugin config for uninstall");
            }

            PluginContext context = new()
            {
                ProductId = product.ProductId,
                InstanceId = product.InstanceId,
                InstanceUniqueId = product.InstanceUniqueId ?? string.Empty,
                Version = product.Version,
                PreviousVersion = product.Version,
                InstallPath = product.InstalledPath,
                StorkConfigDirectory = StorkPaths.ConfigDir,
                ConfigValues = savedConfigValues,
                ProductMetadata = manifest.Metadata is not null
                    ? new Dictionary<string, string>(manifest.Metadata)
                    : new Dictionary<string, string>(),
                Log = message => _logger.LogInformation("[Plugin] {Message}", message),
            };

            foreach (StorkPluginInfo pluginInfo in manifest.Plugins)
            {
                try
                {
                    string assemblyPath = Path.GetFullPath(
                        Path.Combine(storkPluginsDir, pluginInfo.Assembly)
                    );
                    if (!File.Exists(assemblyPath))
                    {
                        _logger.LogWarning(
                            "{Phase}: Plugin assembly not found at {Path}",
                            phase,
                            assemblyPath
                        );
                        continue;
                    }

                    _logger.LogInformation(
                        "Running {Phase} for {TypeName}",
                        phase,
                        pluginInfo.TypeName
                    );

                    loadContexts.Add(
                        await Task.Run(() =>
                            ExecutePluginPhaseAsync(
                                assemblyPath,
                                pluginInfo.TypeName,
                                phase,
                                context
                            )
                        )
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{Phase} failed for {TypeName}: {Message}",
                        phase,
                        pluginInfo.TypeName,
                        ex.Message
                    );

                    if (phase == "PostUninstall")
                    {
                        throw new InvalidOperationException(
                            $"PostUninstall failed for {pluginInfo.TypeName}: {ex.Message}. "
                                + "Database entries may not have been cleaned up.",
                            ex
                        );
                    }
                }
            }
        }
        catch (Exception ex) when (phase == "PreUninstall")
        {
            _logger.LogWarning(ex, "Could not run {Phase} plugins", phase);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> ExecutePluginPhaseAsync(
        string assemblyPath,
        string typeName,
        string phase,
        PluginContext context
    )
    {
        string originalDir = Path.GetDirectoryName(assemblyPath)!;
        string tempDir = Path.Combine(StorkPaths.PluginTempDir, $"uninstall-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            CopyDirectoryRecursive(originalDir, tempDir);

            string tempAssemblyPath = Path.Combine(
                tempDir,
                Path.GetRelativePath(originalDir, assemblyPath)
            );

            ProductPluginLoadContext loadContext = new(tempDir);
            WeakReference loadContextReference = new WeakReference(loadContext);
            try
            {
                System.Reflection.Assembly assembly = loadContext.LoadFromStream(
                    new System.IO.MemoryStream(File.ReadAllBytes(tempAssemblyPath))
                );
                Type? pluginType = assembly.GetType(typeName, throwOnError: true);

                if (
                    pluginType is null
                    || Activator.CreateInstance(pluginType) is not IStorkPlugin plugin
                )
                    return loadContextReference;

                if (phase == "PreUninstall")
                {
                    PluginPreInstallResult result = await plugin.PreUninstallAsync(
                        context,
                        CancellationToken.None
                    );
                    if (!result.Success)
                        _logger.LogWarning("{Phase} failed: {Message}", phase, result.Message);
                }
                else if (phase == "PostUninstall")
                {
                    await plugin.PostUninstallAsync(context, CancellationToken.None);
                }

                try
                {
                    plugin.Cleanup();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Plugin cleanup failed for {TypeName}", typeName);
                }

                return loadContextReference;
            }
            finally
            {
                loadContext.Unload();
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                _logger.LogDebug(
                    "Could not clean up temp plugin directory {TempDir}, will be cleaned on restart",
                    tempDir
                );
            }
        }
    }

    internal static void CopyDirectoryRecursive(string source, string destination)
    {
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
        }
    }

    private static async Task<bool> IsSharedInstallLocationAsync(
        InstalledProduct product,
        CancellationToken cancellationToken
    )
    {
        string manifestPath = StorkPaths.ProductManifestFile(
            product.ProductId,
            product.InstanceUniqueId ?? string.Empty
        );
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            ProductManifest? manifest =
                System.Text.Json.JsonSerializer.Deserialize<ProductManifest>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    }
                );
            return manifest?.SharedInstallLocation ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<List<TrackedFile>?> LoadFileManifestEntriesAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        string manifestPath = StorkPaths.FileManifestPath(productId, uniqueId);
        if (!File.Exists(manifestPath))
        {
            string legacyPath = StorkPaths.LegacyFileManifestPath(productId);
            if (File.Exists(legacyPath))
                manifestPath = legacyPath;
            else
                return null;
        }

        try
        {
            return await FileManifestStore.ReadAsync(manifestPath, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<string>?> LoadFileManifestAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        string manifestPath = StorkPaths.FileManifestPath(productId, uniqueId);

        if (!File.Exists(manifestPath))
        {
            string legacyPath = StorkPaths.LegacyFileManifestPath(productId);
            if (File.Exists(legacyPath))
                manifestPath = legacyPath;
            else
                return null;
        }

        try
        {
            return await FileManifestStore.ReadPathsAsync(manifestPath, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteFileManifest(string productId, string uniqueId)
    {
        try
        {
            string manifestPath = StorkPaths.FileManifestPath(productId, uniqueId);
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);

            // Also clean up legacy path if it exists
            string legacyPath = StorkPaths.LegacyFileManifestPath(productId);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static void CleanupEmptyDirectories(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return;

        try
        {
            foreach (
                string dir in Directory
                    .GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)
            )
            {
                try
                {
                    if (
                        Directory.Exists(dir)
                        && Directory.GetFiles(dir).Length == 0
                        && Directory.GetDirectories(dir).Length == 0
                    )
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            if (
                Directory.Exists(rootPath)
                && Directory.GetFiles(rootPath).Length == 0
                && Directory.GetDirectories(rootPath).Length == 0
            )
            {
                Directory.Delete(rootPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private void RemoveShortcuts(string productTitle, string installPath)
    {
        string programsFolder = ProgramsFolder;

        try
        {
            StartMenuShortcutCleaner.RemoveProductShortcuts(
                programsFolder,
                productTitle,
                installPath,
                StartMenuShortcutCleaner.ReadWithShell
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Start menu shortcuts of {Title} could not be removed",
                productTitle
            );
        }
    }

    private void ResolveLockedFilesChoice(
        IReadOnlyList<LockedFileInfo> initiallyLocked,
        List<string> trackedPaths,
        string directory,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<LockedFileInfo> locked = initiallyLocked;
        while (locked.Count > 0)
        {
            LockedFilesAction action = OnLockedFilesDetected!(locked, _fileLockDetector, directory);
            if (action != LockedFilesAction.Retry)
            {
                _deferLockedFiles = true;
                return;
            }

            locked = _fileLockDetector.GetLockedFiles(trackedPaths, cancellationToken);
        }
    }

    private async Task DeleteFileWithRetryAsync(
        string filePath,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < MaxRetries && !_deferLockedFiles)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                    continue;
                }

                if (!_deferLockedFiles && OnLockedFilesDetected is not null)
                {
                    IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
                        [filePath],
                        cancellationToken
                    );
                    while (lockedFiles.Count > 0)
                    {
                        LockedFilesAction action = OnLockedFilesDetected(
                            lockedFiles,
                            _fileLockDetector,
                            Path.GetDirectoryName(filePath)!
                        );
                        if (action != LockedFilesAction.Retry)
                        {
                            _deferLockedFiles = true;
                            break;
                        }

                        try
                        {
                            File.Delete(filePath);
                            return;
                        }
                        catch (Exception retryEx)
                            when (retryEx is IOException or UnauthorizedAccessException)
                        {
                            lockedFiles = _fileLockDetector.GetLockedFiles(
                                [filePath],
                                cancellationToken
                            );
                        }
                    }
                }

                _logger.LogWarning(
                    "Cannot delete {File}, scheduling for removal on reboot",
                    Path.GetFileName(filePath)
                );
                try
                {
                    string delName = $"DEL_{Guid.NewGuid():N}_{Path.GetFileName(filePath)}";
                    string delPath = Path.Combine(Path.GetDirectoryName(filePath)!, delName);
                    File.Move(filePath, delPath);
                    if (_deferredFileOps.ScheduleDeleteOnReboot(delPath))
                        RequiresReboot = true;
                    else
                        _logger.LogWarning(
                            "Could not schedule {File} for deletion on reboot; it stays on disk as {DeferredName}",
                            Path.GetFileName(filePath),
                            delName
                        );
                }
                catch (Exception renameEx)
                {
                    _logger.LogWarning(
                        renameEx,
                        "Could not defer delete for {File}, skipping",
                        Path.GetFileName(filePath)
                    );
                }
            }
        }
    }

    private async Task RemoveEnvironmentVariablesAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<AppliedEnvironmentVariable> notRemoved = await _envVarService.RemoveTrackedAsync(
                productId,
                uniqueId,
                cancellationToken
            );
            if (notRemoved.Count > 0)
                _logger.LogWarning(
                    "{Count} environment variable change(s) of {ProductId} could not be removed and stay tracked",
                    notRemoved.Count,
                    productId
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Environment variables of {ProductId} could not be removed",
                productId
            );
        }
    }
}
