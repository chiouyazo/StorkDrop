namespace StorkDrop.Contracts.Models;

public sealed record UpdateInfo(
    string Version,
    string DownloadUrl,
    string? ReleaseNotes,
    DateTimeOffset? ReleaseDate
);
