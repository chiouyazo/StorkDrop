namespace StorkDrop.Core.Models;

/// <summary>
/// Describes a plugin assembly and type to load for a product.
/// </summary>
public sealed record StorkPluginInfo(string Assembly, string TypeName);
