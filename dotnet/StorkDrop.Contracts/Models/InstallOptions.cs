using StorkDrop.Contracts.Services;

namespace StorkDrop.Contracts.Models;

/// <summary>
/// Configuration options for a product installation operation.
/// </summary>
public sealed record InstallOptions(
    string TargetPath,
    string InstanceId = InstanceIdHelper.DefaultInstanceId,
    bool CreateBackup = true,
    Dictionary<string, string>? PluginConfigValues = null,
    string? FeedId = null,
    bool SkipFileHandlers = false
);
