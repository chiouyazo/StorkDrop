namespace StorkDrop.Contracts.Models;

/// <summary>
/// Describes a Start Menu shortcut to create for an installed product.
/// </summary>
public sealed record ShortcutInfo(string ExeName, string DisplayName, string? IconPath = null);
