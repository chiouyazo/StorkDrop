namespace StorkDrop.Contracts.Models;

public sealed record InstalledProduct(
    string ProductId,
    string Title,
    string Version,
    string InstalledPath,
    DateTime InstalledDate,
    string? FeedId = null,
    string? BackupPath = null,
    InstallType? InstallType = null,
    string? BadgeText = null,
    string? BadgeColor = null
);
