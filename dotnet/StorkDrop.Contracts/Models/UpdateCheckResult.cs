namespace StorkDrop.Contracts.Models;

public sealed record UpdateCheckResult(
    string ProductId,
    string Title,
    string CurrentVersion,
    string AvailableVersion,
    ProductManifest Manifest
);
