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

    public static string PluginConfigFile(string pluginId) =>
        Path.Combine(ConfigDir, $"plugin-settings-{pluginId}.json");
}
