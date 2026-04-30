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
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IProductRepository _productRepository;
    private readonly IActivityLog _activityLog;
    private readonly IFileLockDetector _fileLockDetector;
    private readonly EnvironmentVariableService _envVarService;
    private readonly DeferredFileOps _deferredFileOps;
    private readonly ILogger<UninstallService> _logger;

    /// <summary>
    /// Set to true after an uninstall deferred locked files for reboot deletion.
    /// </summary>
    public bool RequiresReboot { get; private set; }

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
        ILogger<UninstallService> logger
    )
    {
        _productRepository = productRepository;
        _activityLog = activityLog;
        _fileLockDetector = fileLockDetector;
        _envVarService = envVarService;
        _deferredFileOps = deferredFileOps;
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

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 5, "Running pre-uninstall hooks...")
        );
        await RunUninstallPluginPhaseAsync(product, "PreUninstall", cancellationToken);

        // Force GC to release plugin assembly file locks after PreUninstall
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 10, "Checking for locked files...")
        );
        if (Directory.Exists(product.InstalledPath))
        {
            IReadOnlyList<LockedFileInfo> lockedFiles = _fileLockDetector.GetLockedFiles(
                product.InstalledPath
            );
            if (lockedFiles.Count > 0)
            {
                if (OnLockedFilesDetected is not null)
                {
                    OnLockedFilesDetected(lockedFiles, _fileLockDetector, product.InstalledPath);
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
            product.InstanceId,
            cancellationToken
        );

        if (Directory.Exists(product.InstalledPath))
        {
            List<string>? trackedFiles = await LoadFileManifestAsync(
                product.ProductId,
                product.InstanceId,
                cancellationToken
            );

            if (trackedFiles is not null && trackedFiles.Count > 0)
            {
                _logger.LogInformation(
                    "Deleting {Count} tracked files for {ProductId}",
                    trackedFiles.Count,
                    product.ProductId
                );

                int totalFiles = trackedFiles.Count;
                int deletedCount = 0;
                foreach (string relativePath in trackedFiles)
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
            else
            {
                _logger.LogWarning(
                    "No file manifest found for {ProductId}, deleting entire directory",
                    product.ProductId
                );

                progress?.Report(
                    new InstallProgress(
                        InstallStage.Uninstalling,
                        40,
                        "Deleting installation directory..."
                    )
                );
                await DeleteDirectoryWithRetryAsync(product.InstalledPath, cancellationToken);
            }

            DeleteFileManifest(product.ProductId, product.InstanceId);
        }

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 80, "Removing shortcuts...")
        );
        RemoveShortcuts(product.Title);

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

        progress?.Report(
            new InstallProgress(InstallStage.Uninstalling, 90, "Running post-uninstall hooks...")
        );
        await RunUninstallPluginPhaseAsync(product, "PostUninstall", cancellationToken);

        progress?.Report(new InstallProgress(InstallStage.Uninstalling, 95, "Cleaning up..."));
        string storkDir = Path.Combine(product.InstalledPath, ".stork");
        if (Directory.Exists(storkDir))
        {
            try
            {
                Directory.Delete(storkDir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete .stork directory");
            }
        }

        progress?.Report(new InstallProgress(InstallStage.Uninstalling, 100, "Uninstall complete"));
        _logger.LogInformation(
            "Uninstall of {ProductId} v{Version} complete",
            product.ProductId,
            product.Version
        );
    }

    private async Task RunUninstallPluginPhaseAsync(
        InstalledProduct product,
        string phase,
        CancellationToken cancellationToken
    )
    {
        string manifestPath = Path.Combine(product.InstalledPath, ".stork", "manifest.json");
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

            string storkPluginsDir = Path.Combine(product.InstalledPath, ".stork");
            PluginContext context = new()
            {
                ProductId = product.ProductId,
                InstanceId = product.InstanceId,
                Version = product.Version,
                InstallPath = product.InstalledPath,
                StorkConfigDirectory = StorkPaths.ConfigDir,
                ConfigValues = new Dictionary<string, string>(),
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

                    // Run the plugin in a non-inlined method on a thread pool thread
                    // so all assembly/plugin references die on the stack before
                    // GC.Collect in the caller, and no SyncContext deadlock occurs.
                    await Task.Run(() =>
                        ExecutePluginPhase(assemblyPath, pluginInfo.TypeName, phase, context)
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{Phase} failed for {TypeName}",
                        phase,
                        pluginInfo.TypeName
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not run {Phase} plugins", phase);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecutePluginPhase(
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
            try
            {
                System.Reflection.Assembly assembly = loadContext.LoadFromAssemblyPath(
                    tempAssemblyPath
                );
                Type? pluginType = assembly.GetType(typeName, throwOnError: true);

                if (
                    pluginType is null
                    || Activator.CreateInstance(pluginType) is not IStorkPlugin plugin
                )
                    return;

                if (phase == "PreUninstall")
                {
                    PluginPreInstallResult result = plugin
                        .PreUninstallAsync(context, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    if (!result.Success)
                        _logger.LogWarning("{Phase} failed: {Message}", phase, result.Message);
                }
                else if (phase == "PostUninstall")
                {
                    plugin
                        .PostUninstallAsync(context, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                try
                {
                    plugin.Cleanup();
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Plugin cleanup failed for {TypeName}", typeName);
                }
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

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), true);
        }
    }

    private static async Task<List<string>?> LoadFileManifestAsync(
        string productId,
        string instanceId,
        CancellationToken cancellationToken
    )
    {
        string manifestPath = StorkPaths.FileManifestPath(productId, instanceId);

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
            string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteFileManifest(string productId, string instanceId)
    {
        try
        {
            string manifestPath = StorkPaths.FileManifestPath(productId, instanceId);
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

    private static void RemoveShortcuts(string productTitle)
    {
        string programsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs"
        );

        if (!Directory.Exists(programsFolder))
            return;

        try
        {
            foreach (string subDir in Directory.GetDirectories(programsFolder))
            {
                try
                {
                    foreach (string lnk in Directory.GetFiles(subDir, "*.lnk"))
                    {
                        if (
                            Path.GetFileNameWithoutExtension(lnk)
                                .Contains(productTitle, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            File.Delete(lnk);
                        }
                    }

                    if (
                        Directory.Exists(subDir)
                        && Directory.GetFiles(subDir).Length == 0
                        && Directory.GetDirectories(subDir).Length == 0
                    )
                    {
                        Directory.Delete(subDir);
                    }
                }
                catch
                {
                    // Best effort per folder
                }
            }
        }
        catch
        {
            // Non critical
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
                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                    continue;
                }

                // All retries exhausted -> defer deletion to reboot
                _logger.LogWarning(
                    "Cannot delete {File}, scheduling for removal on reboot",
                    Path.GetFileName(filePath)
                );
                try
                {
                    string delName = $"DEL_{Guid.NewGuid():N}_{Path.GetFileName(filePath)}";
                    string delPath = Path.Combine(Path.GetDirectoryName(filePath)!, delName);
                    File.Move(filePath, delPath);
                    _deferredFileOps.ScheduleDeleteOnReboot(delPath);
                    RequiresReboot = true;
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
        string instanceId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<AppliedEnvironmentVariable> applied = await _envVarService.LoadAppliedAsync(
                productId,
                instanceId,
                cancellationToken
            );

            if (applied.Count > 0)
                await _envVarService.RemoveAsync(applied);

            _envVarService.DeleteTracking(productId, instanceId);
        }
        catch
        {
            // Best effort
        }
    }

    private async Task DeleteDirectoryWithRetryAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        // Delete files individually (with deferred fallback for locked ones)
        if (Directory.Exists(path))
        {
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                await DeleteFileWithRetryAsync(file, cancellationToken);
            }

            CleanupEmptyDirectories(path);
        }

        // Try to remove the root directory if it's now empty
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Directory {Path} could not be fully removed (locked files deferred to reboot)",
                path
            );
        }
    }
}
