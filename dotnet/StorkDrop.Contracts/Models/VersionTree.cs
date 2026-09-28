namespace StorkDrop.Contracts.Models;

/// <summary>
/// One node in a <see cref="VersionTree"/> cascade. Leaf nodes carry the full version string to install.
/// </summary>
/// <param name="Segment">Raw segment value at this depth (e.g. "20260915").</param>
/// <param name="Display">Formatted value for display (e.g. "2026-09-15"); equals <see cref="Segment"/> when unformatted.</param>
/// <param name="FullVersion">The complete version string, set only on leaf nodes; null on inner nodes.</param>
/// <param name="Children">Child nodes at the next depth; empty on leaves.</param>
public sealed record VersionNode(
    string Segment,
    string Display,
    string? FullVersion,
    IReadOnlyList<VersionNode> Children
);

/// <summary>
/// A cascade of a channel's versions split into ordered, dependent segments. Produced by
/// <see cref="Services.VersionGrouping"/>; when it returns null the caller shows the flat list instead.
/// </summary>
/// <param name="Levels">
/// Per-depth column labels from a matching <see cref="VersionSchema"/>, or null for generic labels.
/// </param>
/// <param name="Roots">Top-level nodes (the first segment's distinct values).</param>
public sealed record VersionTree(IReadOnlyList<string>? Levels, IReadOnlyList<VersionNode> Roots);
