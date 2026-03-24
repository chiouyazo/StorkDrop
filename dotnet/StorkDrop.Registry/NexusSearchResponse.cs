using System.Text.Json.Serialization;

namespace StorkDrop.Registry;

/// <summary>
/// Response from the Nexus asset search API.
/// </summary>
public sealed class NexusSearchResponse
{
    /// <summary>
    /// Gets or sets the assets returned by the search.
    /// </summary>
    [JsonPropertyName("items")]
    public NexusAsset[] Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the continuation token for paginated results.
    /// </summary>
    [JsonPropertyName("continuationToken")]
    public string? ContinuationToken { get; set; }
}
