namespace StorkDrop.Registry;

/// <summary>
/// Represents a repository returned by the Nexus /service/rest/v1/repositories endpoint.
/// </summary>
public sealed class NexusRepositoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
