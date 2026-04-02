using StorkDrop.Contracts.Models;

namespace StorkDrop.App.Services;

public sealed record ResolvedPostProduct(ProductManifest Manifest, string FeedId, string FeedName);
