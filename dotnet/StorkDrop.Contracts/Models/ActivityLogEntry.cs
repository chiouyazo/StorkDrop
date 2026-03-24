namespace StorkDrop.Contracts.Models;

public sealed record ActivityLogEntry(
    string Id,
    DateTime Timestamp,
    string Action,
    string ProductId,
    string Details,
    bool Success
);
