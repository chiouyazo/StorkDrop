using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

public interface IRegistryClient
{
    Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    );
    Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    );
    Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    );
    Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    );
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
