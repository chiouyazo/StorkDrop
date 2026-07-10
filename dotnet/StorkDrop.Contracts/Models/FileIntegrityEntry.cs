namespace StorkDrop.Contracts.Models;

/// <summary>One file's verification outcome: its install-relative path and status.</summary>
public sealed record FileIntegrityEntry(string Path, FileIntegrityStatus Status);
