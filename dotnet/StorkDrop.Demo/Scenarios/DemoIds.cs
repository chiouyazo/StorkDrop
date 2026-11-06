namespace StorkDrop.Demo.Scenarios;

internal static class DemoIds
{
    public const string FeedInternal = "internal";
    public const string FeedPartner = "partner";
    public const string FeedCliStable = "cli-stable";
    public const string FeedCliBeta = "cli-beta";
    public const string FeedCliEpic = "cli-epic";
    public const string FeedCliNightly = "cli-nightly";
    public const string FeedInstallErrors = "scn-install";
    public const string FeedUpdateScenarios = "scn-update";
    public const string FeedBranchStable = "br-stable";
    public const string FeedBranchEpic = "br-epic";
    public const string FeedBranchFeature = "br-feature";
    public const string FeedBranchMain = "br-main";
    public const string FeedLocked = "scn-locked";
    public const string FeedDown = "scn-down";
    public const string FeedUnauthorized = "scn-401";
    public const string FeedAddedLater = "scn-new";

    public const string LockedFeedPassword = "demo";

    public const string InstallPreInstallFails = "demo-install-preinstall-fails";
    public const string InstallPostInstallFails = "demo-install-postinstall-fails";
    public const string InstallDownloadRetry = "demo-install-download-retry";
    public const string InstallDownloadFails = "demo-install-download-fails";
    public const string InstallDiskFull = "demo-install-disk-full";
    public const string InstallChecksum = "demo-install-checksum";
    public const string InstallMinHost = "demo-install-min-host";
    public const string InstallElevationSlow = "demo-install-elevation-slow";
    public const string InstallDetailThrows = "demo-install-detail-throws";
    public const string InstallLockConflict = "demo-install-lock-conflict";
    public const string InstallPluginConfig = "demo-install-plugin-config";
    public const string InstallLockedFeed = "demo-install-locked-feed";

    public const string UpdatePostInstallFails = "demo-update-postinstall-fails";
    public const string UpdateCancel = "demo-update-cancel";
    public const string UpdateLockedFiles = "demo-update-locked-files";
    public const string UpdateLockConflict = "demo-update-lock-conflict";
    public const string UpdateStale = "demo-update-stale";
    public const string UpdateChecksum = "demo-update-checksum";
    public const string UpdateLockedFeed = "demo-update-locked-feed";
    public const string UninstallLockedFiles = "demo-uninstall-locked-files";
    public const string UninstallReboot = "demo-uninstall-reboot";
    public const string DependencyCore = "demo-dep-core";
    public const string DependencyAddOn = "demo-dep-addon";
    public const string Integrity = "demo-integrity";
    public const string MultiInstance = "demo-multi-instance";

    public const string BranchCard = "demo-branch-card";
    public const string BranchSwitch = "demo-branch-switch";
    public const string BranchEpicUpdate = "demo-branch-epic-update";
    public const string VersionOrder = "demo-version-order";
    public const string NewFeedProduct = "demo-new-feed-product";

    public const string NovaDashboard = "nova-dashboard";
    public const string NovaCliTools = "nova-cli-tools";
    public const string NovaReporting = "nova-reporting";
    public const string ZetaSync = "zetasync-module";
    public const string NovaExampleData = "nova-example-data";
    public const string NovaDbMigration = "nova-db-migration";
}
