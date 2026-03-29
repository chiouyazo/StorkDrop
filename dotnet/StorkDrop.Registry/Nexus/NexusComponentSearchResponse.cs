using System.Text.Json.Serialization;

namespace StorkDrop.Registry.Nexus;

/// <summary>
/// Response from the Nexus component search API.
/// </summary>
public sealed class NexusComponentSearchResponse
{
    /// <summary>
    /// Gets or sets the components returned by the search.
    /// </summary>
    [JsonPropertyName("items")]
    public NexusComponent[] Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the continuation token for paginated results.
    /// </summary>
    [JsonPropertyName("continuationToken")]
    public string? ContinuationToken { get; set; }
}
