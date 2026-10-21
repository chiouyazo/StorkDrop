using Microsoft.Win32;

namespace StorkDrop.Contracts.Services;

public static class StorkPaths
{
    /// <summary>
    /// The installation's folder name, e.g. "StorkDrop" or a white-labelled "acme-StorkDrop". Read at
    /// access time so it reflects the branding initialized during startup regardless of type-load order.
    /// </summary>
    private static string AppFolder => Branding.Current.AppFolderName;

    /// <summary>
    /// Whether StorkDrop's state (config, installed products, logs, backups, ...) is stored machine-wide
    /// (%ProgramData%) rather than per-user (%AppData%/%LocalAppData%). This is an install-time choice the
    /// installer persists in <c>HKLM\SOFTWARE\{AppFolder}\DataLocation = "Machine"</c>, so a system-wide
    /// install on a terminal server can share one state across all users. Resolved lazily (after branding
    /// is initialized) and cached; any failure falls back to the safe per-user default.
    /// </summary>
    private static readonly Lazy<bool> MachineWide = new Lazy<bool>(ResolveMachineWide);

    private static bool ResolveMachineWide()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return false;

            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\{AppFolder}");
            return key?.GetValue("DataLocation") is string value
                && string.Equals(value, "Machine", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when StorkDrop stores its state machine-wide (%ProgramData%) rather than per-user.</summary>
    public static bool IsMachineWideStorage => MachineWide.Value;

    /// <summary>Roaming-equivalent base: %ProgramData% when machine-wide, otherwise %AppData%.</summary>
    private static string DataRoot =>
        MachineWide.Value
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Local (non-roaming) base: %ProgramData% when machine-wide, otherwise %LocalAppData%.</summary>
    private static string LocalDataRoot =>
        MachineWide.Value
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ConfigDir => Path.Combine(DataRoot, AppFolder, "Config");

    public static string StorkConfigDir => Path.Combine(DataRoot, AppFolder, "Stork", "Config");

    public static string LogDir => Path.Combine(DataRoot, AppFolder, "Logs");

    public static string LogFile => Path.Combine(LogDir, "storkdrop-.log");

    public static string InstalledProductsFile =>
        Path.Combine(StorkConfigDir, "installed-products.json");

    public static string ActivityLogFile => Path.Combine(StorkConfigDir, "activity-log.json");

    public static string BackupRoot => Path.Combine(LocalDataRoot, AppFolder, "Backups");

    /// <summary>Stable, per-machine identifier used in outbound feed status reports.</summary>
    public static string MachineIdFile => Path.Combine(StorkConfigDir, "machine-id");

    /// <summary>On-disk queue of pending feed status reports awaiting delivery.</summary>
    public static string FeedReportSpoolDir =>
        Path.Combine(LocalDataRoot, AppFolder, "ReportSpool");

    public static string TempDir => Path.Combine(Path.GetTempPath(), AppFolder);

    public static string PluginTempDir =>
        Path.Combine(Path.GetTempPath(), AppFolder, "plugin-temp");

    public static string PluginsDirectory => Path.Combine(AppContext.BaseDirectory, "plugins");

    public static string DefaultInstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            AppFolder
        );

    /// <summary>
    /// Gets the plugin settings file path for a StorkDrop platform plugin.
    /// </summary>
    public static string PluginConfigFile(string pluginId) =>
        Path.Combine(ConfigDir, $"plugin-settings-{pluginId}.json");

    /// <summary>
    /// Gets the file manifest path for a specific product instance.
    /// Uses the 8-char InstanceUniqueId, not the user-facing InstanceId.
    /// </summary>
    public static string FileManifestPath(string productId, string uniqueId) =>
        Path.Combine(StorkConfigDir, $"{productId}_{uniqueId}.files.json");

    /// <summary>
    /// Per-product-instance metadata directory holding the product manifest copy, plugin DLLs and
    /// stored file-handler files used for uninstall and re-execute. It lives under StorkDrop's own
    /// config, NOT inside the product's install directory, so products that share an install folder
    /// never overwrite or delete each other's metadata.
    /// </summary>
    public static string ProductMetadataDir(string productId, string uniqueId) =>
        Path.Combine(StorkConfigDir, "metadata", $"{productId}_{uniqueId}");

    /// <summary>The product manifest copy for a specific instance (inside <see cref="ProductMetadataDir"/>).</summary>
    public static string ProductManifestFile(string productId, string uniqueId) =>
        Path.Combine(ProductMetadataDir(productId, uniqueId), "manifest.json");

    /// <summary>The plugin DLL directory for a specific instance (inside <see cref="ProductMetadataDir"/>).</summary>
    public static string ProductPluginsDir(string productId, string uniqueId) =>
        Path.Combine(ProductMetadataDir(productId, uniqueId), "plugins");

    /// <summary>The stored file-handler files directory for a specific instance (inside <see cref="ProductMetadataDir"/>).</summary>
    public static string ProductHandledFilesDir(string productId, string uniqueId) =>
        Path.Combine(ProductMetadataDir(productId, uniqueId), "files");

    /// <summary>
    /// Gets the plugin configuration values path for a specific product instance.
    /// Uses the 8-char InstanceUniqueId, not the user-facing InstanceId.
    /// </summary>
    public static string InstancePluginConfigPath(string productId, string uniqueId) =>
        Path.Combine(StorkConfigDir, $"plugin-config-{productId}_{uniqueId}.json");

    /// <summary>
    /// Gets the environment variable tracking path for a specific product instance.
    /// Uses the 8-char InstanceUniqueId, not the user-facing InstanceId.
    /// </summary>
    public static string EnvVarsPath(string productId, string uniqueId) =>
        Path.Combine(StorkConfigDir, $"{productId}_{uniqueId}.envvars.json");

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
