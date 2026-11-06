using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Scenarios;

namespace StorkDrop.Demo.Data;

internal static class DemoProducts
{
    private const string StableColor = "#2E7D32";
    private const string EpicColor = "#8E24AA";
    private const string FeatureColor = "#EF6C00";

    public static readonly ProductManifest NovaDashboard = new(
        ProductId: DemoIds.NovaDashboard,
        Title: "Nova Dashboard",
        Version: "2.1.0",
        ReleaseDate: new DateOnly(2026, 4, 1),
        InstallType: InstallType.Suite,
        Description: "Analytics dashboard with SQL migration support and real-time monitoring.",
        ReleaseNotes: "Added real-time monitoring panel, fixed chart rendering on high-DPI displays.",
        RecommendedInstallPath: @"{DemoPath}\Nova\Dashboard",
        Publisher: "Nova Software",
        DownloadSizeBytes: 52_428_800,
        Requirements: ["Windows 10+", ".NET 10 Runtime"],
        Shortcuts:
        [
            new ShortcutInfo("NovaDashboard.exe", "Nova Dashboard"),
            new ShortcutInfo("NovaAdmin.exe", "Nova Dashboard Admin", "admin.ico"),
        ],
        ShortcutFolder: "Nova Software",
        OptionalPostProducts:
        [
            new OptionalPostProduct(DemoIds.NovaExampleData, HideNoAccess: false),
        ],
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "STABLE",
        BadgeColor: StableColor
    );

    public static readonly ProductManifest NovaCliTools = new(
        ProductId: DemoIds.NovaCliTools,
        Title: "Nova CLI Tools",
        Version: "2.0.0-rc.3",
        ReleaseDate: new DateOnly(2026, 3, 15),
        InstallType: InstallType.Plugin,
        Description: "Command-line utilities for Nova platform management and automation.",
        ReleaseNotes: "Major version bump with new batch processing commands.",
        RecommendedInstallPath: @"{DemoPath}\CLI",
        Publisher: "Nova Software",
        DownloadSizeBytes: 8_300_000,
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "RC",
        BadgeColor: "#FF9800",
        VersionSchema: new VersionSchema(
            "-",
            [
                new VersionPart("Release"),
                new VersionPart("Build"),
                new VersionPart("Date", "date:yyyyMMdd"),
                new VersionPart("Revision"),
            ]
        )
    );

    public static readonly ProductManifest NovaReporting = new(
        ProductId: DemoIds.NovaReporting,
        Title: "Nova Reporting Module",
        Version: "1.3.0-dev.42",
        ReleaseDate: new DateOnly(2026, 3, 28),
        InstallType: InstallType.Plugin,
        Description: "Reporting engine with database configuration, scheduled report generation, and email delivery. Requires Nova Dashboard.",
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
        RequiredProductIds: [DemoIds.NovaDashboard],
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "DEV",
        BadgeColor: "#E53935"
    );

    public static readonly ProductManifest ZetaSyncModule = new(
        ProductId: DemoIds.ZetaSync,
        Title: "ZetaSync Module",
        Version: "1.0.0-beta.2",
        ReleaseDate: new DateOnly(2026, 2, 20),
        InstallType: InstallType.Plugin,
        Description: "Synchronizes data between Nova Dashboard and external systems via REST API.",
        ReleaseNotes: "Initial release with bidirectional sync support.",
        RecommendedInstallPath: @"{DemoPath}\ZetaSync",
        Publisher: "Zeta Integrations",
        DownloadSizeBytes: 4_200_000,
        RequiredProductIds: [DemoIds.NovaDashboard, DemoIds.NovaCliTools],
        CleanupInfo: new CleanupInfo([], []),
        BadgeText: "BETA",
        BadgeColor: "#7B1FA2"
    );

    public static readonly ProductManifest NovaExampleData = new(
        ProductId: DemoIds.NovaExampleData,
        Title: "Nova Example Data",
        Version: "1.0.0",
        ReleaseDate: new DateOnly(2026, 3, 1),
        InstallType: InstallType.Executable,
        Description: "Sample datasets for development and testing of the Nova Dashboard.",
        ReleaseNotes: "Initial release with customer and transaction sample data.",
        RecommendedInstallPath: @"{DemoPath}\ExampleData",
        Publisher: "Nova Software",
        DownloadSizeBytes: 2_100_000,
        Plugins:
        [
            new StorkPluginInfo("plugins/Nova.ExampleData.dll", "Nova.ExampleData.Installer"),
        ],
        CleanupInfo: new CleanupInfo([], [])
    );

    public static readonly ProductManifest NovaDbMigration = new(
        ProductId: DemoIds.NovaDbMigration,
        Title: "Nova DB Migration",
        Version: "1.0.0",
        ReleaseDate: new DateOnly(2026, 4, 5),
        InstallType: InstallType.Executable,
        Description: "Runs database schema migrations and seed data insertion for the Nova platform.",
        ReleaseNotes: "Initial release with schema v3 migration and seed data.",
        RecommendedInstallPath: @"{DemoPath}\Migrations",
        Publisher: "Nova Software",
        DownloadSizeBytes: 1_200_000,
        Plugins:
        [
            new StorkPluginInfo("plugins/Nova.DbMigration.dll", "Nova.DbMigration.MigrationPlugin"),
        ],
        CleanupInfo: new CleanupInfo([], [])
    );

    public static ProductManifest MultiInstance =>
        Scenario(
            DemoIds.MultiInstance,
            "Demo: Multiple instances",
            "1.0.0",
            "Can be installed several times under different instance names.",
            allowMultipleInstances: true
        );

    public static ProductManifest Scenario(
        string productId,
        string title,
        string version,
        string description,
        string? releaseNotes = null,
        string? badgeText = null,
        string? badgeColor = null,
        bool withPlugin = false,
        string[]? requiredProductIds = null,
        string? minHostVersion = null,
        long downloadSizeBytes = 24_000_000,
        string? contentSha256 = null,
        bool allowMultipleInstances = false,
        InstallType installType = InstallType.Executable,
        string? recommendedInstallPath = null
    ) =>
        new ProductManifest(
            ProductId: productId,
            Title: title,
            Version: version,
            ReleaseDate: new DateOnly(2026, 11, 1),
            InstallType: installType,
            Description: description,
            ReleaseNotes: releaseNotes ?? $"Demo release notes for {title} {version}.",
            RecommendedInstallPath: recommendedInstallPath
                ?? $@"{{DemoPath}}\Scenarios\{productId}",
            Publisher: "StorkDrop Demo",
            DownloadSizeBytes: downloadSizeBytes,
            Plugins: withPlugin
                ?
                [
                    new StorkPluginInfo(
                        $"plugins/{productId}.dll",
                        $"StorkDrop.Demo.Scenarios.{productId}"
                    ),
                ]
                : null,
            RequiredProductIds: requiredProductIds,
            CleanupInfo: new CleanupInfo([], []),
            BadgeText: badgeText,
            BadgeColor: badgeColor,
            AllowMultipleInstances: allowMultipleInstances,
            ContentSha256: contentSha256,
            MinHostVersion: minHostVersion
        );

    public static ProductManifest Branch(
        string productId,
        string title,
        string version,
        string badgeText,
        string badgeColor,
        string description
    ) =>
        Scenario(
            productId,
            title,
            version,
            description,
            releaseNotes: $"Build {version} ({badgeText}).",
            badgeText: badgeText,
            badgeColor: badgeColor
        );

    public static string BadgeColorFor(string badgeText) =>
        badgeText switch
        {
            "STABLE" => StableColor,
            "EPIC" => EpicColor,
            "FEATURE" => FeatureColor,
            _ => "#607D8B",
        };
}
