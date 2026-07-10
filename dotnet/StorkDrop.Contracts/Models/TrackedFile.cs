namespace StorkDrop.Contracts.Models;

/// <summary>
/// One file that StorkDrop installed from a product's content archive, recorded in the per-instance
/// file manifest. <see cref="Sha256"/> is the hex hash captured at install time; it is null for
/// legacy manifests written before integrity tracking existed (those files are unverifiable).
/// </summary>
public sealed record TrackedFile(string Path, string? Sha256 = null, long Size = 0);
