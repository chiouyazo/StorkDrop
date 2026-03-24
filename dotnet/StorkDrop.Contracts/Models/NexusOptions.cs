namespace StorkDrop.Contracts.Models;

public sealed class NexusOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Repository { get; set; } = "storkdrop-releases";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
