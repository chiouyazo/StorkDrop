namespace StorkDrop.Demo.Scenarios;

internal static class DemoBehaviors
{
    private static readonly Dictionary<string, DemoBehavior> ByProduct = new Dictionary<
        string,
        DemoBehavior
    >(StringComparer.OrdinalIgnoreCase)
    {
        [DemoIds.InstallPreInstallFails] = new DemoBehavior
        {
            SkipConfigDialog = true,
            PreInstallError =
                "Database 'NovaDB' on db-prod-01 is not reachable: Login timeout expired (SQL Server error -2).",
        },
        [DemoIds.InstallPostInstallFails] = new DemoBehavior
        {
            SkipConfigDialog = true,
            PostInstallError =
                "Windows service 'NovaReportingService' did not reach the Running state within 30 seconds.",
        },
        [DemoIds.InstallDownloadRetry] = new DemoBehavior { TransientDownloadFailures = 2 },
        [DemoIds.InstallDownloadFails] = new DemoBehavior { DownloadAlwaysFails = true },
        [DemoIds.InstallDiskFull] = new DemoBehavior { DiskFull = true },
        [DemoIds.InstallChecksum] = new DemoBehavior { ChecksumMismatch = true },
        [DemoIds.InstallElevationSlow] = new DemoBehavior { ElevationSlow = true },
        [DemoIds.UpdatePostInstallFails] = new DemoBehavior
        {
            SkipConfigDialog = true,
            PostInstallError =
                "Windows service 'NovaReportingService' did not reach the Running state within 30 seconds.",
        },
        [DemoIds.UpdateCancel] = new DemoBehavior { SlowUpdate = true },
        [DemoIds.UpdateChecksum] = new DemoBehavior { ChecksumMismatch = true },
        [DemoIds.UninstallReboot] = new DemoBehavior { RebootOnUninstall = true },
    };

    public static DemoBehavior For(string productId) =>
        ByProduct.TryGetValue(productId, out DemoBehavior? behavior)
            ? behavior
            : new DemoBehavior();
}
