using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

/// <summary>
/// Result of resolving the version a user approved. <see cref="Approved"/> is set when the feed still
/// serves exactly that version; otherwise <see cref="CurrentLatest"/> is the feed's current latest.
/// </summary>
public sealed record ApprovedUpdateResolution(
    ProductManifest? Approved,
    ProductManifest? CurrentLatest
);
