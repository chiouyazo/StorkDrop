namespace StorkDrop.Contracts.Services;

public static class StorkPaths
{
    private static readonly string AppData = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData
    );

    private static readonly string LocalAppData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData
    );

    public static string ConfigDir { get; } = Path.Combine(AppData, "StorkDrop", "Config");

    public static string StorkConfigDir { get; } =
        Path.Combine(AppData, "StorkDrop", "Stork", "Config");

    public static string LogDir { get; } = Path.Combine(AppData, "StorkDrop", "Logs");

    public static string LogFile { get; } = Path.Combine(LogDir, "storkdrop-.log");

    public static string InstalledProductsFile { get; } =
        Path.Combine(StorkConfigDir, "installed-products.json");

    public static string ActivityLogFile { get; } =
        Path.Combine(StorkConfigDir, "activity-log.json");

    public static string BackupRoot { get; } = Path.Combine(LocalAppData, "StorkDrop", "Backups");

    public static string TempDir { get; } = Path.Combine(Path.GetTempPath(), "StorkDrop");

    public static string PluginTempDir { get; } =
        Path.Combine(Path.GetTempPath(), "StorkDrop", "plugin-temp");

    public static string PluginsDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "plugins");

    public static string DefaultInstallRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "StorkDrop"
        );

    /// <summary>
    /// Gets the plugin settings file path for a StorkDrop platform plugin.
    /// </summary>
    public static string PluginConfigFile(string pluginId) =>
        Path.Combine(ConfigDir, $"plugin-settings-{pluginId}.json");

    /// <summary>
    /// Gets the file manifest path for a specific product instance.
    /// </summary>
    public static string FileManifestPath(string productId, string instanceId) =>
        Path.Combine(StorkConfigDir, $"{productId}_{instanceId}.files.json");

    /// <summary>
    /// Gets the plugin configuration values path for a specific product instance.
    /// </summary>
    public static string InstancePluginConfigPath(string productId, string instanceId) =>
        Path.Combine(StorkConfigDir, $"plugin-config-{productId}_{instanceId}.json");

    /// <summary>
    /// Gets the environment variable tracking path for a specific product instance.
    /// </summary>
    public static string EnvVarsPath(string productId, string instanceId) =>
        Path.Combine(StorkConfigDir, $"{productId}_{instanceId}.envvars.json");

    /// <summary>
    /// Gets the legacy file manifest path (pre-instance-aware).
    /// Used as fallback during migration from older versions.
    /// </summary>
    public static string LegacyFileManifestPath(string productId) =>
        Path.Combine(StorkConfigDir, $"{productId}.files.json");

    /// <summary>
    /// Gets the legacy plugin config path (pre-instance-aware).
    /// Used as fallback during migration from older versions.
    /// </summary>
    public static string LegacyPluginConfigPath(string productId) =>
        Path.Combine(StorkConfigDir, $"plugin-config-{productId}.json");

    /// <summary>
    /// Gets the legacy environment variable tracking path (pre-instance-aware).
    /// Used as fallback during migration from older versions.
    /// </summary>
    public static string LegacyEnvVarsPath(string productId) =>
        Path.Combine(StorkConfigDir, $"{productId}.envvars.json");
}
