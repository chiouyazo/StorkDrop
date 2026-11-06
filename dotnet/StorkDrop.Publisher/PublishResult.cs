using StorkDrop.Contracts.Models;

namespace StorkDrop.Publisher;

/// <summary>Outcome of a publish: the stored manifest and whether the root "latest" manifest was replaced.</summary>
public sealed record PublishResult(
    ProductManifest Manifest,
    bool LatestUpdated,
    string? PreviousLatestVersion
);
