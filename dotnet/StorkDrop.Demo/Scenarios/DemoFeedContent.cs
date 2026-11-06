using System.Net;
using System.Net.Http;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Demo.Scenarios;

/// <summary>The mutable content of one demo feed: published versions per product and the "latest" root manifest.</summary>
internal sealed class DemoFeedContent
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, List<ProductManifest>> _versions = new Dictionary<
        string,
        List<ProductManifest>
    >(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _latest = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> _failingVersionLookups = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    );

    public DemoFeedContent(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public DemoFeedFailure Failure { get; set; }

    public void Publish(ProductManifest manifest, bool makeLatest = true)
    {
        lock (_gate)
        {
            if (!_versions.TryGetValue(manifest.ProductId, out List<ProductManifest>? list))
                _versions[manifest.ProductId] = list = [];

            list.RemoveAll(m =>
                string.Equals(m.Version, manifest.Version, StringComparison.Ordinal)
            );
            list.Add(manifest);
            if (makeLatest)
                _latest[manifest.ProductId] = manifest.Version;
        }
    }

    public void PublishVersions(
        ProductManifest template,
        IEnumerable<string> versions,
        string? badgeText = null,
        string? badgeColor = null,
        string? latest = null
    )
    {
        List<string> ordered = versions.ToList();
        foreach (string version in ordered)
        {
            ProductManifest manifest = template with
            {
                Version = version,
                BadgeText = badgeText ?? template.BadgeText,
                BadgeColor = badgeColor ?? template.BadgeColor,
            };
            Publish(manifest, makeLatest: false);
        }

        string latestVersion =
            latest ?? ordered.OrderByDescending(v => v, VersionComparer.Instance).First();
        lock (_gate)
            _latest[template.ProductId] = latestVersion;
    }

    public void FailVersionLookups(string productId)
    {
        lock (_gate)
            _failingVersionLookups.Add(productId);
    }

    public void Withdraw(string productId, string version)
    {
        lock (_gate)
        {
            if (_versions.TryGetValue(productId, out List<ProductManifest>? list))
                list.RemoveAll(m => string.Equals(m.Version, version, StringComparison.Ordinal));
        }
    }

    public void SetLatest(string productId, string version)
    {
        lock (_gate)
            _latest[productId] = version;
    }

    public string? LatestVersion(string productId)
    {
        lock (_gate)
            return _latest.GetValueOrDefault(productId);
    }

    public IReadOnlyList<ProductManifest> GetLatestManifests()
    {
        lock (_gate)
        {
            List<ProductManifest> result = [];
            foreach ((string productId, string latest) in _latest)
            {
                ProductManifest? manifest = Find(productId, latest);
                if (manifest is not null)
                    result.Add(manifest);
            }

            return result;
        }
    }

    public ProductManifest? Find(string productId, string? version)
    {
        lock (_gate)
        {
            if (!_versions.TryGetValue(productId, out List<ProductManifest>? list))
                return null;

            string? wanted = version ?? _latest.GetValueOrDefault(productId);
            return list.FirstOrDefault(m =>
                string.Equals(m.Version, wanted, StringComparison.Ordinal)
            );
        }
    }

    public IReadOnlyList<string> Versions(string productId)
    {
        lock (_gate)
        {
            return _versions.TryGetValue(productId, out List<ProductManifest>? list)
                ? list.Select(m => m.Version).ToList()
                : [];
        }
    }

    public bool FailsVersionLookup(string productId, string? version)
    {
        lock (_gate)
        {
            return version is not null
                && _failingVersionLookups.Contains(productId)
                && !string.Equals(
                    version,
                    _latest.GetValueOrDefault(productId),
                    StringComparison.Ordinal
                );
        }
    }

    public async Task EnsureReachableAsync(CancellationToken cancellationToken)
    {
        switch (Failure)
        {
            case DemoFeedFailure.Unreachable:
                await Task.Delay(700, cancellationToken);
                throw new HttpRequestException(
                    $"No connection could be made because the target machine actively refused it. ({Id}.demo.invalid:443)"
                );
            case DemoFeedFailure.Unauthorized:
                throw new HttpRequestException(
                    "Response status code does not indicate success: 401 (Unauthorized).",
                    null,
                    HttpStatusCode.Unauthorized
                );
        }
    }
}
