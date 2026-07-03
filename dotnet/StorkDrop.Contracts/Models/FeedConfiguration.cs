namespace StorkDrop.Contracts.Models;

public sealed record FeedConfiguration(
    string Id,
    string Name,
    string Url,
    string? Repository,
    string? Username,
    string? EncryptedPassword,
    string? PluginId,
    string? LockPasswordHash = null,
    string? ReportUrl = null,
    string? EncryptedReportSecret = null,
    string? ReportCustomerId = null
);
