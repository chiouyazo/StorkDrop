namespace StorkDrop.Contracts.Models;

/// <summary>
/// A single installed product instance included in a <see cref="FeedReport"/>.
/// </summary>
public sealed record FeedReportProduct(
    string ProductId,
    string Title,
    string Version,
    string? Channel,
    string InstanceId,
    DateTime InstalledDate
);
