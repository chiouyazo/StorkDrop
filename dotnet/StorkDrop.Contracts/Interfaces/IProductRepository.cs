using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

public interface IProductRepository
{
    Task<IReadOnlyList<InstalledProduct>> GetAllAsync(
        CancellationToken cancellationToken = default
    );
    Task<InstalledProduct?> GetByIdAsync(
        string productId,
        CancellationToken cancellationToken = default
    );
    Task AddAsync(InstalledProduct product, CancellationToken cancellationToken = default);
    Task UpdateAsync(InstalledProduct product, CancellationToken cancellationToken = default);
    Task RemoveAsync(string productId, CancellationToken cancellationToken = default);
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
