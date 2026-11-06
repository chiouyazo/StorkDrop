using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

/// <summary>
/// Wraps IInstallationEngine with a cross-process lock per product instance and guaranteed cleanup.
/// Each installation is isolated - failures never affect other installs.
/// </summary>
public sealed class InstallationCoordinator
{
    private readonly IInstallationEngine _engine;
    private readonly UninstallService _uninstallService;
    private readonly ILogger<InstallationCoordinator> _logger;

    public InstallationCoordinator(
        IInstallationEngine engine,
        UninstallService uninstallService,
        ILogger<InstallationCoordinator> logger
    )
    {
        _engine = engine;
        _uninstallService = uninstallService;
        _logger = logger;
    }

    /// <summary>
    /// Provides access to the underlying engine for non-install operations
    /// (e.g., GetPluginConfigurationAsync).
    /// </summary>
    public IInstallationEngine Engine => _engine;

    public async Task<InstallResult> InstallWithIsolationAsync(
        ProductManifest manifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        using ProductLockAcquisition productLock = ProductOperationLock.TryAcquire(
            manifest.ProductId,
            options.InstanceId,
            "install"
        );

        if (!productLock.Acquired)
        {
            _logger.LogWarning(
                "Install of {ProductId} blocked - another operation is already in progress",
                manifest.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = productLock.DescribeConflict(manifest.Title),
                FailedStep = "Coordination",
            };
        }

        try
        {
            _logger.LogInformation(
                "Install lock acquired for {ProductId}, starting installation",
                manifest.ProductId
            );
            return await _engine.InstallAsync(manifest, options, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Install of {ProductId} was cancelled", manifest.ProductId);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Installation cancelled.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Install of {ProductId} failed with unhandled exception",
                manifest.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FailedStep = "Unknown",
                Exception = ex,
            };
        }
    }

    public async Task<InstallResult> UpdateWithIsolationAsync(
        InstalledProduct installed,
        ProductManifest newManifest,
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        using ProductLockAcquisition productLock = ProductOperationLock.TryAcquire(
            installed.ProductId,
            installed.InstanceId,
            "update"
        );

        if (!productLock.Acquired)
        {
            _logger.LogWarning(
                "Update of {ProductId} blocked - another operation is already in progress",
                installed.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = productLock.DescribeConflict(installed.Title),
                FailedStep = "Coordination",
            };
        }

        try
        {
            _logger.LogInformation("Update lock acquired for {ProductId}", installed.ProductId);
            await _engine.UpdateAsync(installed, newManifest, options, progress, cancellationToken);
            return new InstallResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update of {ProductId} was cancelled", installed.ProductId);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Update cancelled.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Update of {ProductId} failed with unhandled exception",
                installed.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FailedStep = "Unknown",
                Exception = ex,
            };
        }
    }

    public async Task UninstallWithIsolationAsync(
        InstalledProduct product,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        using ProductLockAcquisition productLock = ProductOperationLock.TryAcquire(
            product.ProductId,
            product.InstanceId,
            "uninstall"
        );

        if (!productLock.Acquired)
            throw new InvalidOperationException(productLock.DescribeConflict(product.Title));

        _logger.LogInformation("Uninstall lock acquired for {ProductId}", product.ProductId);
        await _uninstallService.UninstallAsync(product, progress, cancellationToken);
    }

    public Task UninstallWithIsolationAsync(
        InstalledProduct product,
        CancellationToken cancellationToken
    )
    {
        return UninstallWithIsolationAsync(product, null, cancellationToken);
    }

    public async Task<InstallResult> ReExecutePluginsWithIsolationAsync(
        InstalledProduct product,
        ReExecuteOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken
    )
    {
        using ProductLockAcquisition productLock = ProductOperationLock.TryAcquire(
            product.ProductId,
            product.InstanceId,
            "re-execute"
        );

        if (!productLock.Acquired)
        {
            _logger.LogWarning(
                "Re-execute of {ProductId} blocked - another operation is already in progress",
                product.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = productLock.DescribeConflict(product.Title),
                FailedStep = "Coordination",
            };
        }

        try
        {
            _logger.LogInformation("Re-execute lock acquired for {ProductId}", product.ProductId);
            return await _engine.ReExecutePluginsAsync(
                product,
                options,
                progress,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Re-execute of {ProductId} was cancelled", product.ProductId);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = "Operation cancelled.",
                FailedStep = "Cancelled",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Re-execute of {ProductId} failed with unhandled exception",
                product.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FailedStep = "Unknown",
                Exception = ex,
            };
        }
    }
}
