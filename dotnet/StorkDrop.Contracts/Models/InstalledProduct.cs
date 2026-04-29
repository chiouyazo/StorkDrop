namespace StorkDrop.Contracts.Models;

/// <summary>
/// Represents a product instance that has been installed on the system.
/// Identity is determined by the combination of <see cref="ProductId"/> and <see cref="InstanceId"/>.
/// </summary>
public sealed record InstalledProduct(
    string ProductId,
    string InstanceId,
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
