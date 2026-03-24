using StorkDrop.Core.Models;

namespace StorkDrop.Core.Interfaces;

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
