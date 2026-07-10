using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Verifies and repairs an installed product's files against the hashes recorded at install time.
/// Only files tracked in the product's file manifest (i.e. those installed from its content archive)
/// are ever inspected or written; foreign files in the install directory are never touched.
/// </summary>
public interface IIntegrityService
{
    /// <summary>Checks every tracked file of the instance and reports which are OK, modified or missing.</summary>
    Task<IntegrityReport> VerifyAsync(
        InstalledProduct product,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Re-downloads the installed version and restores the given tracked files (relative paths),
    /// overwriting only those. Returns the number of files actually restored.
    /// </summary>
    Task<int> RepairAsync(
        InstalledProduct product,
        IReadOnlyList<string> relativePaths,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    );
}
