using System.IO;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoRegistryClient : IRegistryClient
{
    private readonly IReadOnlyList<ProductManifest> _products;

    public DemoRegistryClient(IReadOnlyList<ProductManifest> products)
    {
        _products = products;
    }

    public Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_products);

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_products.FirstOrDefault(p => p.ProductId == productId));

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        ProductManifest? product = _products.FirstOrDefault(p => p.ProductId == productId);
        if (product is null)
            return Task.FromResult<ProductManifest?>(null);

        return Task.FromResult<ProductManifest?>(product with { Version = version });
    }

    public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        ProductManifest? product = _products.FirstOrDefault(p => p.ProductId == productId);
        if (product is null)
            return Task.FromResult<IReadOnlyList<string>>([]);

        return Task.FromResult<IReadOnlyList<string>>(["1.0.0", "1.1.0", product.Version]);
    }

    public Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
