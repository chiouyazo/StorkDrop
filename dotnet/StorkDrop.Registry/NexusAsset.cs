using System.Text.Json.Serialization;

namespace StorkDrop.Registry;

/// <summary>
/// Represents a single asset in a Nexus repository.
/// </summary>
public sealed class NexusAsset
{
    /// <summary>
    /// Gets or sets the asset identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository this asset belongs to.
    /// </summary>
    [JsonPropertyName("repository")]
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the asset path within the repository.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the download URL for this asset.
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type of this asset.
    /// </summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;
}
