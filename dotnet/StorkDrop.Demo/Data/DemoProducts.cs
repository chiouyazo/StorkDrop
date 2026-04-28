using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Data;

internal static class DemoProducts
{
    public static readonly ProductManifest NovaDashboard = new(
        ProductId: "nova-dashboard",
        Title: "Nova Dashboard",
        Version: "2.1.0",
        ReleaseDate: new DateOnly(2026, 4, 1),
        InstallType: InstallType.Suite,
        Description: "Analytics dashboard with SQL migration support and real-time monitoring.",
        ReleaseNotes: "Added real-time monitoring panel, fixed chart rendering on high-DPI displays.",
        RecommendedInstallPath: @"C:\Users\Demo\NovaApps\Dashboard",
        Publisher: "Nova Software",
        DownloadSizeBytes: 52_428_800,
        Requirements: ["Windows 10+", ".NET 10 Runtime"],
        Shortcuts:
        [
            new ShortcutInfo("NovaDashboard.exe", "Nova Dashboard"),
            new ShortcutInfo("NovaAdmin.exe", "Nova Dashboard Admin", "admin.ico"),
        ],
        ShortcutFolder: "Nova Software",
        OptionalPostProducts: [new OptionalPostProduct("nova-example-data", HideNoAccess: false)],
        CleanupInfo: new CleanupInfo([], [@"%APPDATA%\Demo\NovaApps\Dashboard"]),
        BadgeText: "STABLE",
        BadgeColor: "#2E7D32"
    );

    public static readonly ProductManifest NovaCliTools = new(
        ProductId: "nova-cli-tools",
        Title: "Nova CLI Tools",
        Version: "2.0.0-rc.3",
        ReleaseDate: new DateOnly(2026, 3, 15),
        InstallType: InstallType.Plugin,
        Description: "Command-line utilities for Nova platform management and automation.",
        ReleaseNotes: "Major version bump with new batch processing commands.",
        RecommendedInstallPath: @"{DemoPath}\CLI",
        Publisher: "Nova Software",
        DownloadSizeBytes: 8_300_000,
        EnvironmentVariables:
        [
            new EnvironmentVariableInfo("NOVA_HOME", "{InstallPath}", "set"),
            new EnvironmentVariableInfo("PATH", @"{InstallPath}\bin", "append"),
        ],
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "RC",
        BadgeColor: "#FF9800"
    );

    public static readonly ProductManifest NovaReporting = new(
        ProductId: "nova-reporting",
        Title: "Nova Reporting Module",
        Version: "1.3.0-dev.42",
        ReleaseDate: new DateOnly(2026, 3, 28),
        InstallType: InstallType.Plugin,
        Description: "Reporting engine with database configuration, scheduled report generation, and email delivery.",
        ReleaseNotes: "Added email delivery, improved PDF export quality.",
        RecommendedInstallPath: @"{DemoPath}\Reporting",
        Publisher: "Nova Software",
        DownloadSizeBytes: 15_700_000,
        Plugins:
        [
            new StorkPluginInfo(
                "plugins/Nova.Reporting.Installer.dll",
                "Nova.Reporting.Installer.ReportingSetup"
            ),
        ],
        RequiredProductIds: ["nova-dashboard"],
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "DEV",
        BadgeColor: "#E53935"
    );

    public static readonly ProductManifest ZetaSyncModule = new(
        ProductId: "zetasync-module",
        Title: "ZetaSync Module",
        Version: "1.0.0-beta.2",
        ReleaseDate: new DateOnly(2026, 2, 20),
        InstallType: InstallType.Plugin,
        Description: "Synchronizes data between Nova Dashboard and external systems via REST API.",
        ReleaseNotes: "Initial release with bidirectional sync support.",
        RecommendedInstallPath: @"C:\Users\Demo\NovaApps\ZetaSync",
        Publisher: "Zeta Integrations",
        DownloadSizeBytes: 4_200_000,
        RequiredProductIds: ["nova-dashboard", "nova-cli-tools"],
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "BETA",
        BadgeColor: "#7B1FA2"
    );

    public static readonly ProductManifest NovaExampleData = new(
        ProductId: "nova-example-data",
        Title: "Nova Example Data",
        Version: "1.0.0",
        ReleaseDate: new DateOnly(2026, 3, 1),
        InstallType: InstallType.Executable,
        Description: "Sample datasets for development and testing of the Nova Dashboard.",
        ReleaseNotes: "Initial release with customer and transaction sample data.",
        RecommendedInstallPath: @"C:\Users\Demo\NovaApps\ExampleData",
        Publisher: "Nova Software",
        DownloadSizeBytes: 2_100_000,
        Plugins:
        [
            new StorkPluginInfo("plugins/Nova.ExampleData.dll", "Nova.ExampleData.Installer"),
        ],
        CleanupInfo: new CleanupInfo([], [])
    );

    public static readonly ProductManifest NovaDbMigration = new(
        ProductId: "nova-db-migration",
        Title: "Nova DB Migration",
        Version: "1.0.0",
        ReleaseDate: new DateOnly(2026, 4, 5),
        InstallType: InstallType.Executable,
        Description: "Runs database schema migrations and seed data insertion for the Nova platform.",
        ReleaseNotes: "Initial release with schema v3 migration and seed data.",
        RecommendedInstallPath: @"C:\Users\Demo\NovaApps\Migrations",
        Publisher: "Nova Software",
        DownloadSizeBytes: 1_200_000,
        Plugins:
        [
            new StorkPluginInfo("plugins/Nova.DbMigration.dll", "Nova.DbMigration.MigrationPlugin"),
        ],
        CleanupInfo: new CleanupInfo([], [])
    );

    public static IReadOnlyList<ProductManifest> InternalFeedProducts =>
        [NovaDashboard, NovaCliTools, NovaReporting, NovaDbMigration];

    public static IReadOnlyList<ProductManifest> PartnerFeedProducts =>
        [ZetaSyncModule, NovaExampleData];

    public static InstalledProduct PreInstalledCliTools =>
        new(
            ProductId: "nova-cli-tools",
            Title: "Nova CLI Tools",
            Version: "2.0.0-rc.2",
            InstalledPath: @"C:\Users\Demo\StorkDrop\CLI",
            InstalledDate: new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            FeedId: "internal"
        );
}
