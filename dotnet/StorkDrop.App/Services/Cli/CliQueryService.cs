using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.App.Services.Cli;

internal sealed class CliQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IFeedRegistry _feedRegistry;
    private readonly IProductRepository _productRepository;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CliQueryService(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        TextWriter output,
        TextWriter error
    )
    {
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _output = output;
        _error = error;
    }

    public async Task<int> ListJsonAsync(string? feedToken)
    {
        IReadOnlyList<FeedInfo> feeds = CliFeedSelector.Select(_feedRegistry, feedToken);
        IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync();
        List<ListProduct> products = [];
        List<QueryError> errors = [];

        foreach (FeedInfo feed in feeds)
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                IReadOnlyList<ProductManifest> available = await client.GetAllProductsAsync();
                foreach (ProductManifest manifest in available)
                {
                    List<InstanceRef> instances = installed
                        .Where(i => i.ProductId == manifest.ProductId)
                        .OrderBy(i => i.InstanceId, StringComparer.OrdinalIgnoreCase)
                        .Select(i => new InstanceRef(i.InstanceId, i.InstanceUniqueId, i.Version))
                        .ToList();
                    products.Add(
                        new ListProduct(
                            manifest.ProductId,
                            manifest.Title,
                            manifest.Version,
                            manifest.InstallType,
                            feed.Id,
                            feed.Name,
                            manifest.BadgeText,
                            manifest.BadgeColor,
                            instances
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                errors.Add(new QueryError(feed.Id, null, null, ex.Message));
                _error.WriteLine($"Failed to load products from feed '{feed.Name}': {ex.Message}");
            }
        }

        WriteJson(new ListResult(products, errors));
        return errors.Count == 0 ? CliExitCodes.Success : CliExitCodes.FeedError;
    }

    public async Task<int> VersionsAsync(string productId, string? feedToken, bool json)
    {
        IReadOnlyList<FeedInfo> feeds = CliFeedSelector.Select(_feedRegistry, feedToken);
        List<FeedVersions> found = [];
        List<QueryError> errors = [];

        foreach (FeedInfo feed in feeds)
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                IReadOnlyList<string> versions = await client.GetAvailableVersionsAsync(productId);
                if (versions.Count > 0)
                    found.Add(new FeedVersions(feed.Id, feed.Name, versions));
            }
            catch (Exception ex)
            {
                errors.Add(new QueryError(feed.Id, productId, null, ex.Message));
                _error.WriteLine($"Failed to query feed '{feed.Name}': {ex.Message}");
            }
        }

        if (json)
        {
            WriteJson(new VersionsResult(productId, found, errors));
        }
        else
        {
            foreach (FeedVersions entry in found)
            {
                _output.WriteLine($"Versions for '{productId}' on feed '{entry.FeedName}':");
                foreach (string version in entry.Versions)
                    _output.WriteLine($"  {version}");
            }
        }

        if (errors.Count > 0)
            return CliExitCodes.FeedError;

        if (found.Count == 0)
        {
            _error.WriteLine($"Product '{productId}' not found in any configured feed.");
            return CliExitCodes.NotFound;
        }

        return CliExitCodes.Success;
    }

    public async Task<int> InstalledAsync(string? feedToken, bool json)
    {
        IReadOnlyList<FeedInfo> allFeeds = _feedRegistry.GetFeeds();
        HashSet<string>? selectedFeedIds = SelectedFeedIds(feedToken);
        IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync();

        List<InstalledEntry> entries = installed
            .Where(p => IsInFeeds(p, selectedFeedIds))
            .OrderBy(p => p.ProductId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToEntry(p, allFeeds))
            .ToList();

        if (json)
        {
            WriteJson(new InstalledResult(entries));
            return CliExitCodes.Success;
        }

        _output.WriteLine(
            $"{"Product ID", -40} {"Title", -30} {"Version", -16} {"Instance", -16} {"Feed", -20} {"Badge"}"
        );
        _output.WriteLine(new string('-', 135));
        foreach (InstalledEntry entry in entries)
        {
            _output.WriteLine(
                $"{entry.ProductId, -40} {entry.Title, -30} {entry.Version, -16} {entry.InstanceId, -16} {entry.FeedName ?? entry.FeedId ?? "", -20} {entry.BadgeText ?? ""}"
            );
        }
        return CliExitCodes.Success;
    }

    public async Task<int> OutdatedAsync(string? feedToken, bool json)
    {
        IReadOnlyList<FeedInfo> allFeeds = _feedRegistry.GetFeeds();
        HashSet<string>? selectedFeedIds = SelectedFeedIds(feedToken);
        IReadOnlyList<InstalledProduct> installed = await _productRepository.GetAllAsync();

        List<OutdatedEntry> outdated = [];
        List<QueryError> errors = [];
        Dictionary<string, ProductManifest?> lookups = new Dictionary<string, ProductManifest?>(
            StringComparer.OrdinalIgnoreCase
        );

        IEnumerable<InstalledProduct> candidates = installed
            .Where(p => IsInFeeds(p, selectedFeedIds))
            .OrderBy(p => p.ProductId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.InstanceId, StringComparer.OrdinalIgnoreCase);

        foreach (InstalledProduct product in candidates)
        {
            if (string.IsNullOrEmpty(product.FeedId))
            {
                errors.Add(
                    new QueryError(
                        null,
                        product.ProductId,
                        product.InstanceId,
                        "Installed without a feed id, its own feed is unknown."
                    )
                );
                continue;
            }

            FeedInfo? feed = allFeeds.FirstOrDefault(f =>
                string.Equals(f.Id, product.FeedId, StringComparison.OrdinalIgnoreCase)
            );
            if (feed is null)
            {
                errors.Add(
                    new QueryError(
                        product.FeedId,
                        product.ProductId,
                        product.InstanceId,
                        $"Feed '{product.FeedId}' is not configured."
                    )
                );
                continue;
            }

            string key = $"{feed.Id}\n{product.ProductId}";
            if (!lookups.TryGetValue(key, out ProductManifest? latest))
            {
                try
                {
                    IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                    latest = await client.GetProductManifestAsync(product.ProductId);
                }
                catch (Exception ex)
                {
                    errors.Add(
                        new QueryError(feed.Id, product.ProductId, product.InstanceId, ex.Message)
                    );
                    _error.WriteLine(
                        $"Failed to query feed '{feed.Name}' for '{product.ProductId}': {ex.Message}"
                    );
                    continue;
                }
                lookups[key] = latest;
            }

            if (latest is null)
            {
                errors.Add(
                    new QueryError(
                        feed.Id,
                        product.ProductId,
                        product.InstanceId,
                        $"Product not found in feed '{feed.Name}'."
                    )
                );
                continue;
            }

            if (
                !string.IsNullOrEmpty(latest.Version)
                && VersionComparer.IsNewer(latest.Version, product.Version)
            )
            {
                outdated.Add(
                    new OutdatedEntry(
                        product.ProductId,
                        product.InstanceId,
                        product.InstanceUniqueId,
                        product.Title,
                        product.Version,
                        latest.Version,
                        feed.Id,
                        feed.Name,
                        product.BadgeText,
                        product.BadgeColor
                    )
                );
            }
        }

        if (json)
        {
            WriteJson(new OutdatedResult(outdated, errors));
        }
        else
        {
            _output.WriteLine(
                $"{"Product ID", -40} {"Instance", -16} {"Installed", -16} {"Available", -16} {"Feed", -20}"
            );
            _output.WriteLine(new string('-', 112));
            foreach (OutdatedEntry entry in outdated)
            {
                _output.WriteLine(
                    $"{entry.ProductId, -40} {entry.InstanceId, -16} {entry.InstalledVersion, -16} {entry.AvailableVersion, -16} {entry.FeedName, -20}"
                );
            }
            foreach (QueryError error in errors)
            {
                _error.WriteLine(
                    $"Could not check {error.ProductId} ({error.InstanceId}): {error.Message}"
                );
            }
        }

        return errors.Count == 0 ? CliExitCodes.Success : CliExitCodes.FeedError;
    }

    private HashSet<string>? SelectedFeedIds(string? feedToken)
    {
        if (string.IsNullOrWhiteSpace(feedToken))
            return null;

        return CliFeedSelector
            .Select(_feedRegistry, feedToken)
            .Select(f => f.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsInFeeds(InstalledProduct product, HashSet<string>? feedIds) =>
        feedIds is null || (product.FeedId is not null && feedIds.Contains(product.FeedId));

    private static InstalledEntry ToEntry(InstalledProduct product, IReadOnlyList<FeedInfo> feeds)
    {
        FeedInfo? feed = feeds.FirstOrDefault(f =>
            string.Equals(f.Id, product.FeedId, StringComparison.OrdinalIgnoreCase)
        );
        return new InstalledEntry(
            product.ProductId,
            product.InstanceId,
            product.InstanceUniqueId,
            product.Title,
            product.Version,
            product.InstalledPath,
            product.InstalledDate,
            product.FeedId,
            feed?.Name,
            product.BadgeText,
            product.BadgeColor
        );
    }

    private void WriteJson<T>(T value) =>
        _output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private sealed record QueryError(
        string? FeedId,
        string? ProductId,
        string? InstanceId,
        string Message
    );

    private sealed record InstanceRef(string InstanceId, string? InstanceUniqueId, string Version);

    private sealed record ListProduct(
        string ProductId,
        string Title,
        string Version,
        InstallType InstallType,
        string FeedId,
        string FeedName,
        string? BadgeText,
        string? BadgeColor,
        IReadOnlyList<InstanceRef> Installed
    );

    private sealed record ListResult(
        IReadOnlyList<ListProduct> Products,
        IReadOnlyList<QueryError> Errors
    );

    private sealed record FeedVersions(
        string FeedId,
        string FeedName,
        IReadOnlyList<string> Versions
    );

    private sealed record VersionsResult(
        string ProductId,
        IReadOnlyList<FeedVersions> Feeds,
        IReadOnlyList<QueryError> Errors
    );

    private sealed record InstalledEntry(
        string ProductId,
        string InstanceId,
        string? InstanceUniqueId,
        string Title,
        string Version,
        string InstalledPath,
        DateTime InstalledDate,
        string? FeedId,
        string? FeedName,
        string? BadgeText,
        string? BadgeColor
    );

    private sealed record InstalledResult(IReadOnlyList<InstalledEntry> Products);

    private sealed record OutdatedEntry(
        string ProductId,
        string InstanceId,
        string? InstanceUniqueId,
        string Title,
        string InstalledVersion,
        string AvailableVersion,
        string FeedId,
        string FeedName,
        string? BadgeText,
        string? BadgeColor
    );

    private sealed record OutdatedResult(
        IReadOnlyList<OutdatedEntry> Products,
        IReadOnlyList<QueryError> Errors
    );
}
