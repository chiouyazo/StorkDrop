namespace StorkDrop.Contracts.Models;

public sealed class EnvironmentManifestProduct
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
    public string? Path { get; set; }
    public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();
}
