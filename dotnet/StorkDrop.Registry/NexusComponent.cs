using System.Text.Json.Serialization;

namespace StorkDrop.Registry;

/// <summary>
/// Represents a single component in a Nexus repository.
/// </summary>
public sealed class NexusComponent
{
    /// <summary>
    /// Gets or sets the component identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository this component belongs to.
    /// </summary>
    [JsonPropertyName("repository")]
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the component format.
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the component group.
    /// </summary>
    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the component name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the component version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assets belonging to this component.
    /// </summary>
    [JsonPropertyName("assets")]
    public NexusAsset[] Assets { get; set; } = [];
}
