namespace StorkDrop.Contracts.Models;

public sealed record InstallOptions(
    string TargetPath,
    bool CreateBackup = true,
    Dictionary<string, string>? PluginConfigValues = null,
    string? FeedId = null
);
