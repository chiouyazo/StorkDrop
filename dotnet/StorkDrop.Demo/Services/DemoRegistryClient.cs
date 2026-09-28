using System.IO;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Demo.Services;

internal sealed class DemoRegistryClient : IRegistryClient
{
    private readonly IReadOnlyList<ProductManifest> _products;
    private readonly IReadOnlyList<string>? _versions;
    private readonly string? _badgeText;
    private readonly string? _badgeColor;

    public DemoRegistryClient(
        IReadOnlyList<ProductManifest> products,
        IReadOnlyList<string>? versions = null,
        string? badgeText = null,
        string? badgeColor = null
    )
    {
        _products = products;
        _versions = versions;
        _badgeText = badgeText;
        _badgeColor = badgeColor;
    }

    private ProductManifest Decorate(ProductManifest manifest) =>
        _badgeText is null && _badgeColor is null
            ? manifest
            : manifest with
            {
                BadgeText = _badgeText ?? manifest.BadgeText,
                BadgeColor = _badgeColor ?? manifest.BadgeColor,
            };

    public Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<ProductManifest>>(_products.Select(Decorate).ToList());

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        ProductManifest? product = _products.FirstOrDefault(p => p.ProductId == productId);
        if (product is null)
            return Task.FromResult<ProductManifest?>(null);

        string latest = _versions is { Count: > 0 }
            ? _versions.OrderByDescending(v => v, VersionComparer.Instance).First()
            : product.Version;

        return Task.FromResult<ProductManifest?>(Decorate(product with { Version = latest }));
    }

    public Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        ProductManifest? product = _products.FirstOrDefault(p => p.ProductId == productId);
        if (product is null)
            return Task.FromResult<ProductManifest?>(null);

        return Task.FromResult<ProductManifest?>(Decorate(product with { Version = version }));
    }

    public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        ProductManifest? product = _products.FirstOrDefault(p => p.ProductId == productId);
        if (product is null)
            return Task.FromResult<IReadOnlyList<string>>([]);

        return Task.FromResult<IReadOnlyList<string>>(
            _versions ?? ["1.0.0", "1.1.0", product.Version]
        );
    }

    public Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
