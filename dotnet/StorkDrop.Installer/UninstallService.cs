using System.Text.Json;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

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
    private readonly ILogger<UninstallService> _logger;

    public UninstallService(
        IProductRepository productRepository,
        IActivityLog activityLog,
        IFileLockDetector fileLockDetector,
        EnvironmentVariableService envVarService,
        ILogger<UninstallService> logger
    )
    {
        _productRepository = productRepository;
        _activityLog = activityLog;
        _fileLockDetector = fileLockDetector;
        _envVarService = envVarService;
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
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Starting uninstall of {ProductId} from {InstalledPath}",
            product.ProductId,
            product.InstalledPath
        );

        // Check for file locks (only .exe and .dll — these are the files that actually get locked
        // by running processes; checking all files causes false positives from transient OS handles)
        if (Directory.Exists(product.InstalledPath))
        {
            _logger.LogDebug("Checking file locks in {InstalledPath}", product.InstalledPath);
            string[] files;
            try
            {
                files = Directory.GetFiles(product.InstalledPath, "*", SearchOption.AllDirectories);
            }
            catch (DirectoryNotFoundException)
            {
                files = Array.Empty<string>();
            }

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                if (
                    !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                if (_fileLockDetector.IsFileLocked(file))
                {
                    IReadOnlyList<string> lockingProcesses = _fileLockDetector.GetLockingProcesses(
                        file
                    );
                    string processNames =
                        lockingProcesses.Count > 0
                            ? string.Join(", ", lockingProcesses)
                            : string.Empty;
                    throw new FileLockedException(Path.GetFileName(file), processNames);
                }
            }
        }

        // Remove environment variables
        _logger.LogDebug("Removing environment variables for {ProductId}", product.ProductId);
        await RemoveEnvironmentVariablesAsync(product.ProductId, cancellationToken);

        // Delete installed files using file manifest if available
        if (Directory.Exists(product.InstalledPath))
        {
            List<string>? trackedFiles = await LoadFileManifestAsync(
                product.ProductId,
                cancellationToken
            );

            if (trackedFiles is not null && trackedFiles.Count > 0)
            {
                _logger.LogInformation(
                    "Deleting {Count} tracked files for {ProductId}",
                    trackedFiles.Count,
                    product.ProductId
                );
                // Delete only tracked files
                foreach (string relativePath in trackedFiles)
                {
                    string fullPath = Path.Combine(product.InstalledPath, relativePath);
                    if (File.Exists(fullPath))
                    {
                        await DeleteFileWithRetryAsync(fullPath, cancellationToken);
                    }
                }

                // Clean up empty directories
                CleanupEmptyDirectories(product.InstalledPath);
            }
            else
            {
                _logger.LogWarning(
                    "No file manifest found for {ProductId}, deleting entire directory",
                    product.ProductId
                );
                // Fallback: delete entire directory if no manifest exists
                await DeleteDirectoryWithRetryAsync(product.InstalledPath, cancellationToken);
            }

            // Delete the file manifest itself
            DeleteFileManifest(product.ProductId);
        }

        // Remove Start Menu shortcuts
        _logger.LogDebug("Removing shortcuts for {ProductTitle}", product.Title);
        RemoveShortcuts(product.Title);

        // Remove from repository
        _logger.LogDebug("Removing {ProductId} from product repository", product.ProductId);
        await _productRepository.RemoveAsync(product.ProductId, cancellationToken);

        // Log the activity
        ActivityLogEntry entry = new ActivityLogEntry(
            Id: Guid.NewGuid().ToString(),
            Timestamp: DateTime.UtcNow,
            Action: "Uninstall",
            ProductId: product.ProductId,
            Details: $"Uninstalled {product.Title} v{product.Version} from {product.InstalledPath}",
            Success: true
        );
        await _activityLog.LogAsync(entry, cancellationToken);

        _logger.LogInformation(
            "Uninstall of {ProductId} v{Version} complete",
            product.ProductId,
            product.Version
        );
    }

    private static async Task<List<string>?> LoadFileManifestAsync(
        string productId,
        CancellationToken cancellationToken
    )
    {
        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StorkDrop",
            "Config"
        );
        string manifestPath = Path.Combine(configDir, $"{productId}.files.json");

        if (!File.Exists(manifestPath))
            return null;

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

    private static void DeleteFileManifest(string productId)
    {
        try
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StorkDrop",
                "Config"
            );
            string manifestPath = Path.Combine(configDir, $"{productId}.files.json");
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);
        }
        catch
        {
            // Best-effort cleanup
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
                    // Best-effort cleanup
                }
            }

            // Try to remove root if empty
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
            // Best-effort cleanup
        }
    }

    private static void RemoveShortcuts(string productTitle)
    {
        // Search in multiple possible shortcut folders
        string programsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs"
        );

        if (!Directory.Exists(programsFolder))
            return;

        try
        {
            // Search all subdirectories for matching shortcuts
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

                    // Remove folder if empty
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
                    // Best-effort per folder
                }
            }
        }
        catch
        {
            // Non-critical
        }
    }

    private static async Task DeleteFileWithRetryAsync(
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
            catch (IOException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private async Task RemoveEnvironmentVariablesAsync(
        string productId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<AppliedEnvironmentVariable> applied = await _envVarService.LoadAppliedAsync(
                productId,
                cancellationToken
            );

            if (applied.Count > 0)
                _envVarService.Remove(applied);

            _envVarService.DeleteTracking(productId);
        }
        catch
        {
            // Best-effort
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
