using StorkDrop.Core.Models;

namespace StorkDrop.Core.Interfaces;

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
}
