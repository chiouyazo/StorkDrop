namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Sends per-feed inventory status reports to each feed's configured report endpoint.
/// Feeds without a report URL are ignored. Implementations must never throw into the
/// calling install/uninstall flow — reporting is best-effort and self-healing.
/// </summary>
public interface IFeedReportService
{
    /// <summary>
    /// Builds a full inventory snapshot for the feed that <paramref name="feedId"/> belongs to
    /// and queues it for delivery. Call after a product from that feed is installed, updated,
    /// or removed. No-op if the feed has no report URL configured.
    /// </summary>
    Task NotifyFeedChangedAsync(string? feedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to deliver any queued reports. Successful ones are removed; failures remain
    /// queued for a later attempt. Safe to call repeatedly and concurrently.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
