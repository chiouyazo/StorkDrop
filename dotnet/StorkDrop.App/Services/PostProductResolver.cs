using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Services;

public sealed class PostProductResolver
{
    private readonly IFeedRegistry _feedRegistry;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<PostProductResolver> _logger;

    public PostProductResolver(
        IFeedRegistry feedRegistry,
        IProductRepository productRepository,
        ILogger<PostProductResolver> logger
    )
    {
        _feedRegistry = feedRegistry;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<PostProductResolution> ResolveAsync(
        OptionalPostProduct[] postProducts,
        CancellationToken cancellationToken = default
    ) => await ResolveAsync(postProducts, null, cancellationToken);

    public async Task<PostProductResolution> ResolveAsync(
        OptionalPostProduct[] postProducts,
        string? requiredBadge,
        CancellationToken cancellationToken = default
    )
    {
        List<ResolvedPostProduct> available = [];
        List<ResolvedPostProduct> alreadyInstalled = [];
        List<string> warnings = [];

        foreach (OptionalPostProduct postProduct in postProducts)
        {
            InstalledProduct? installed = await _productRepository.GetByIdAsync(
                postProduct.Id,
                cancellationToken
            );

            if (installed is not null)
            {
                ResolvedPostProduct? resolvedInstalled = await FindInFeedsAsync(
                    postProduct.Id,
                    requiredBadge,
                    cancellationToken
                );
                if (resolvedInstalled is not null)
                    alreadyInstalled.Add(resolvedInstalled);
                continue;
            }

            ResolvedPostProduct? resolved = await FindInFeedsAsync(
                postProduct.Id,
                requiredBadge,
                cancellationToken
            );

            if (resolved is not null)
            {
                available.Add(resolved);
            }
            else if (!postProduct.HideNoAccess)
            {
                warnings.Add(postProduct.Id);
            }
            else
            {
                _logger.LogDebug(
                    "Optional post-product {ProductId} not found in any feed, hideNoAccess=true",
                    postProduct.Id
                );
            }
        }

        return new PostProductResolution(available, alreadyInstalled, warnings);
    }

    private async Task<ResolvedPostProduct?> FindInFeedsAsync(
        string productId,
        string? requiredBadge,
        CancellationToken cancellationToken
    )
    {
        ResolvedPostProduct? fallback = null;

        foreach (FeedInfo feed in _feedRegistry.GetFeeds())
        {
            try
            {
                IRegistryClient client = _feedRegistry.GetClient(feed.Id);
                ProductManifest? manifest = await client.GetProductManifestAsync(
                    productId,
                    cancellationToken
                );
                if (manifest is null)
                    continue;

                bool badgeMatches = string.Equals(
                    manifest.BadgeText ?? string.Empty,
                    requiredBadge ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase
                );

                if (badgeMatches)
                    return new ResolvedPostProduct(manifest, feed.Id, feed.Name);

                fallback ??= new ResolvedPostProduct(manifest, feed.Id, feed.Name);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to check feed {FeedName} for post-product {ProductId}",
                    feed.Name,
                    productId
                );
            }
        }

        return fallback;
    }
}
