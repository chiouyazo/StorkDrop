using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

/// <summary>
/// Wraps IInstallationEngine with per-product locking and guaranteed cleanup.
/// Each installation is isolated - failures never affect other installs.
/// </summary>
public sealed class InstallationCoordinator
{
    private readonly IInstallationEngine _engine;
    private readonly ILogger<InstallationCoordinator> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _productLocks =
        new ConcurrentDictionary<string, SemaphoreSlim>();

    public InstallationCoordinator(
        IInstallationEngine engine,
        ILogger<InstallationCoordinator> logger
    )
    {
        _engine = engine;
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
        SemaphoreSlim productLock = GetProductLock(manifest.ProductId);

        if (!await productLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            _logger.LogWarning(
                "Install of {ProductId} blocked - another installation is already in progress",
                manifest.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = $"Another installation of {manifest.Title} is already in progress.",
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
        finally
        {
            productLock.Release();
            _logger.LogInformation("Install lock released for {ProductId}", manifest.ProductId);
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
        SemaphoreSlim productLock = GetProductLock(installed.ProductId);

        if (!await productLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            _logger.LogWarning(
                "Update of {ProductId} blocked - another operation is already in progress",
                installed.ProductId
            );
            return new InstallResult
            {
                Success = false,
                ErrorMessage = $"Another operation on {installed.Title} is already in progress.",
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
        finally
        {
            productLock.Release();
            _logger.LogInformation("Update lock released for {ProductId}", installed.ProductId);
        }
    }

    public async Task UninstallWithIsolationAsync(
        InstalledProduct product,
        CancellationToken cancellationToken
    )
    {
        SemaphoreSlim productLock = GetProductLock(product.ProductId);

        if (!await productLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Another operation on {product.Title} is already in progress."
            );
        }

        try
        {
            _logger.LogInformation("Uninstall lock acquired for {ProductId}", product.ProductId);
            await _engine.UninstallAsync(product, cancellationToken);
        }
        finally
        {
            productLock.Release();
            _logger.LogInformation("Uninstall lock released for {ProductId}", product.ProductId);
        }
    }

    private SemaphoreSlim GetProductLock(string productId) =>
        _productLocks.GetOrAdd(productId, _ => new SemaphoreSlim(1, 1));
}
