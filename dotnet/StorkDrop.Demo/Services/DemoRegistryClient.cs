using System.IO;
using System.Net;
using System.Net.Http;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Scenarios;

namespace StorkDrop.Demo.Services;

internal sealed class DemoRegistryClient : IRegistryClient
{
    private readonly DemoFeedContent _content;

    public DemoRegistryClient(DemoFeedContent content)
    {
        _content = content;
    }

    public async Task<IReadOnlyList<ProductManifest>> GetAllProductsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _content.EnsureReachableAsync(cancellationToken);
        return _content.GetLatestManifests();
    }

    public async Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        await _content.EnsureReachableAsync(cancellationToken);
        return _content.Find(productId, null);
    }

    public async Task<ProductManifest?> GetProductManifestAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    )
    {
        await _content.EnsureReachableAsync(cancellationToken);
        if (_content.FailsVersionLookup(productId, version))
            throw new HttpRequestException(
                "Response status code does not indicate success: 503 (Service Unavailable).",
                null,
                HttpStatusCode.ServiceUnavailable
            );

        return _content.Find(productId, version);
    }

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        string productId,
        CancellationToken cancellationToken = default
    )
    {
        await _content.EnsureReachableAsync(cancellationToken);
        return _content
            .Versions(productId)
            .OrderByDescending(v => v, VersionComparer.Instance)
            .ToList();
    }

    public Task<Stream> DownloadProductAsync(
        string productId,
        string version,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<Stream>(new MemoryStream());

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _content.EnsureReachableAsync(cancellationToken);
        return true;
    }
}
