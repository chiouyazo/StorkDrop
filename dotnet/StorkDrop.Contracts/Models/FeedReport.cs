namespace StorkDrop.Contracts.Models;

/// <summary>
/// A full status snapshot for a single feed: the reporting machine's identity plus every
/// product currently installed from that feed. Sent as the data payload of a CloudEvent
/// whenever a product from the feed is installed, updated, or removed. Each report is the
/// complete current state, so a missed report is corrected by the next one.
/// </summary>
public sealed record FeedReport(
    string MachineId,
    string Hostname,
    string OperatingSystem,
    string StorkDropVersion,
    DateTimeOffset SentAt,
    string FeedId,
    string FeedName,
    string? CustomerId,
    IReadOnlyList<FeedReportProduct> Products
);
