using System.Text.Json.Serialization;

namespace StorkDrop.Registry.Nexus;

/// <summary>
/// Represents an item in the Nexus repository content tree.
/// </summary>
public sealed class NexusContentItem
{
    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item path within the repository.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item type (file or directory).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this item is a leaf node (file).
    /// </summary>
    [JsonPropertyName("leaf")]
    public bool Leaf { get; set; }
}
