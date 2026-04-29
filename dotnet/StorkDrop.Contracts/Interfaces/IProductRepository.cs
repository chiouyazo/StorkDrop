using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Persists and retrieves installed product records.
/// Products are identified by the composite key (ProductId, InstanceId).
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Gets all installed product instances.
    /// </summary>
    Task<IReadOnlyList<InstalledProduct>> GetAllAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a specific product instance by product ID and instance ID.
    /// </summary>
    Task<InstalledProduct?> GetByIdAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets all installed instances of a given product.
    /// </summary>
    Task<IReadOnlyList<InstalledProduct>> GetInstancesAsync(
        string productId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Adds or replaces an installed product instance.
    /// </summary>
    Task AddAsync(InstalledProduct product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing installed product instance.
    /// </summary>
    Task UpdateAsync(InstalledProduct product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an installed product instance.
    /// </summary>
    Task RemoveAsync(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initializes the repository by loading data from disk.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads repository data from disk, discarding in-memory state.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
