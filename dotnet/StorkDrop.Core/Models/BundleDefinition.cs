namespace StorkDrop.Core.Models;

public sealed record BundleDefinition(
    string BundleId,
    string Title,
    string[] ProductIds,
    string? Description = null
);
