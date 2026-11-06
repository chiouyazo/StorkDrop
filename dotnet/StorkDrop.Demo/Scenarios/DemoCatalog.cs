using StorkDrop.Contracts.Models;
using StorkDrop.Demo.Data;

namespace StorkDrop.Demo.Scenarios;

internal static class DemoCatalog
{
    private static readonly string[] OldAndNew = ["1.0.0", "1.1.0"];

    public static IReadOnlyList<DemoFeedContent> CreateFeeds()
    {
        DemoFeedContent internalFeed = new DemoFeedContent(DemoIds.FeedInternal, "Internal");
        internalFeed.Publish(DemoProducts.NovaDashboard);
        internalFeed.PublishVersions(
            DemoProducts.NovaCliTools,
            ["2.0.0-rc.1", "2.0.0-rc.2", "2.0.0-rc.3"]
        );
        internalFeed.Publish(DemoProducts.NovaReporting);
        internalFeed.Publish(DemoProducts.NovaDbMigration);
        internalFeed.Publish(DemoProducts.MultiInstance);

        DemoFeedContent partner = new DemoFeedContent(DemoIds.FeedPartner, "Partner");
        partner.Publish(DemoProducts.ZetaSyncModule);
        partner.Publish(DemoProducts.NovaExampleData);

        return
        [
            internalFeed,
            partner,
            CliFeed(
                DemoIds.FeedCliStable,
                "CLI · Stable",
                "STABLE",
                ["2.0.0", "1.9.2", "1.9.1", "1.9.0", "1.8.5"]
            ),
            CliFeed(
                DemoIds.FeedCliBeta,
                "CLI · Beta",
                "BETA",
                ["2.2.0-beta.1", "2.1.0-beta.3", "2.1.0-beta.2", "2.1.0-beta.1", "2.0.0-beta.4"]
            ),
            CliFeed(
                DemoIds.FeedCliEpic,
                "CLI · EPIC",
                "EPIC",
                [
                    "2.35.0-2401-20261001-0900",
                    "2.34.7-2390-20260920-2455",
                    "2.34.7-2386-20260915-2438",
                    "2.34.7-2386-20260915-2401",
                    "2.34.7-2386-20260901-2210",
                ]
            ),
            CliFeed(
                DemoIds.FeedCliNightly,
                "CLI · Nightly",
                "NIGHTLY",
                ["2.3.0-nightly.20261005", "2.3.0-nightly.20261004", "2.3.0-nightly.20261003"]
            ),
            CreateInstallErrorFeed(),
            CreateUpdateFeed(),
            BranchFeed(DemoIds.FeedBranchStable, "Branch / Stable", "2.34.7", "STABLE"),
            BranchFeed(
                DemoIds.FeedBranchEpic,
                "Branch / EPIC",
                "2.35.0-2401-20261001-0900",
                "EPIC"
            ),
            BranchFeed(
                DemoIds.FeedBranchFeature,
                "Branch / Feature",
                "2.36.0-4711-20261105-1200",
                "FEATURE"
            ),
            CreateBranchMainFeed(),
            CreateLockedFeed(),
            new DemoFeedContent(DemoIds.FeedDown, "Demo: Feed unreachable")
            {
                Failure = DemoFeedFailure.Unreachable,
            },
            new DemoFeedContent(DemoIds.FeedUnauthorized, "Demo: Feed unauthorized (401)")
            {
                Failure = DemoFeedFailure.Unauthorized,
            },
        ];
    }

    public static DemoFeedContent CreateAddedLaterFeed()
    {
        DemoFeedContent feed = new DemoFeedContent(
            DemoIds.FeedAddedLater,
            "Demo: Feed added later"
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.NewFeedProduct,
                "Demo: Product from a feed added later",
                "1.0.0",
                "Appears in the Marketplace after the feed configuration was changed from outside the running app."
            )
        );
        return feed;
    }

    public static IReadOnlyList<DemoSeed> CreateSeeds() =>
        [
            new DemoSeed(DemoIds.NovaCliTools, DemoIds.FeedInternal, "2.0.0-rc.2"),
            new DemoSeed(DemoIds.MultiInstance, DemoIds.FeedInternal, "1.0.0"),
            new DemoSeed(DemoIds.UpdatePostInstallFails, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UpdateCancel, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UpdateLockedFiles, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UpdateLockConflict, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UpdateStale, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UpdateChecksum, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.DependencyCore, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.DependencyAddOn, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UninstallLockedFiles, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UninstallReboot, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.Integrity, DemoIds.FeedUpdateScenarios, "1.0.0"),
            new DemoSeed(DemoIds.UpdateLockedFeed, DemoIds.FeedLocked, "1.0.0"),
            new DemoSeed(DemoIds.BranchSwitch, DemoIds.FeedBranchMain, "2.34.7"),
            new DemoSeed(
                DemoIds.BranchEpicUpdate,
                DemoIds.FeedBranchMain,
                "2.34.7-2386-20260915-2401"
            ),
            new DemoSeed(DemoIds.VersionOrder, DemoIds.FeedBranchMain, "2.34.7-2386-20260915-999"),
        ];

    private static DemoFeedContent CliFeed(
        string id,
        string name,
        string badge,
        IReadOnlyList<string> versions
    )
    {
        DemoFeedContent feed = new DemoFeedContent(id, name);
        feed.PublishVersions(
            DemoProducts.NovaCliTools,
            versions,
            badge,
            DemoProducts.BadgeColorFor(badge)
        );
        return feed;
    }

    private static DemoFeedContent BranchFeed(string id, string name, string version, string badge)
    {
        DemoFeedContent feed = new DemoFeedContent(id, name);
        feed.Publish(
            DemoProducts.Branch(
                DemoIds.BranchCard,
                "Demo: Branch badges and channels",
                version,
                badge,
                DemoProducts.BadgeColorFor(badge),
                "Same product on three channels. The Marketplace card keeps the release channel even though the feature build has the highest version."
            )
        );
        return feed;
    }

    private static DemoFeedContent CreateBranchMainFeed()
    {
        DemoFeedContent feed = new DemoFeedContent(DemoIds.FeedBranchMain, "Branch / Hauptfeed");

        ProductManifest switchStable = DemoProducts.Branch(
            DemoIds.BranchSwitch,
            "Demo: Update from STABLE to FEATURE",
            "2.34.7",
            "STABLE",
            DemoProducts.BadgeColorFor("STABLE"),
            "The latest manifest of the feed is a feature build, so the update changes the build label."
        );
        feed.Publish(switchStable, makeLatest: false);
        feed.Publish(
            switchStable with
            {
                Version = "2.35.0-2401-20261001-0900",
                BadgeText = "FEATURE",
                BadgeColor = DemoProducts.BadgeColorFor("FEATURE"),
                ReleaseNotes = "Feature build from branch CRYSYNC-2468.",
            }
        );

        ProductManifest epic = DemoProducts.Branch(
            DemoIds.BranchEpicUpdate,
            "Demo: Update within an EPIC build",
            "2.34.7-2386-20260915-2401",
            "EPIC",
            DemoProducts.BadgeColorFor("EPIC"),
            "Update between two builds of the same EPIC branch: feed name and badge are shown, the badge does not change."
        );
        feed.Publish(epic, makeLatest: false);
        feed.Publish(epic with { Version = "2.34.7-2386-20260915-2438" });

        ProductManifest order = DemoProducts.Branch(
            DemoIds.VersionOrder,
            "Demo: Build 999 to 1000",
            "2.34.7-2386-20260915-999",
            "EPIC",
            DemoProducts.BadgeColorFor("EPIC"),
            "Build 1000 counts as newer than build 999 although the text order says otherwise."
        );
        feed.Publish(order, makeLatest: false);
        feed.Publish(order with { Version = "2.34.7-2386-20260915-1000" });
        return feed;
    }

    private static DemoFeedContent CreateLockedFeed()
    {
        DemoFeedContent feed = new DemoFeedContent(DemoIds.FeedLocked, "Demo: Locked feed");
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallLockedFeed,
                "Demo: Install from locked feed",
                "1.0.0",
                $"The feed asks for a password before install, update, uninstall and repair. Password: {DemoIds.LockedFeedPassword}"
            )
        );
        feed.PublishVersions(
            DemoProducts.Scenario(
                DemoIds.UpdateLockedFeed,
                "Demo: Update from locked feed",
                "1.0.0",
                $"Update and uninstall ask for the feed password. Password: {DemoIds.LockedFeedPassword}"
            ),
            OldAndNew
        );
        return feed;
    }

    private static DemoFeedContent CreateInstallErrorFeed()
    {
        DemoFeedContent feed = new DemoFeedContent(
            DemoIds.FeedInstallErrors,
            "Demo errors: Install"
        );

        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallPreInstallFails,
                "Demo: Install fails at PreInstall",
                "1.0.0",
                "The PreInstall step of the plugin fails. Nothing is copied.",
                withPlugin: true
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallPostInstallFails,
                "Demo: Install fails at PostInstall",
                "1.0.0",
                "Files are copied, then PostInstall fails. A dialog asks to keep or remove the installation.",
                withPlugin: true
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallDownloadRetry,
                "Demo: Download with retries",
                "1.0.0",
                "The download fails twice with a transient error and succeeds on the third attempt. Open the log of the running installation to see the retries.",
                downloadSizeBytes: 48_000_000
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallDownloadFails,
                "Demo: Download fails",
                "1.0.0",
                "The server keeps answering 503. After four attempts the real cause is reported.",
                downloadSizeBytes: 48_000_000
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallDiskFull,
                "Demo: Not enough disk space",
                "1.0.0",
                "The package is larger than the free space of the drive.",
                downloadSizeBytes: 5_497_558_138_880
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallChecksum,
                "Demo: Checksum mismatch",
                "1.0.0",
                "The downloaded package does not match the checksum published in the manifest.",
                contentSha256: "0000000000000000000000000000000000000000000000000000000000000000"
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallMinHost,
                "Demo: Minimum host version not met",
                "1.0.0",
                "The manifest requires StorkDrop 99.0.0.",
                minHostVersion: "99.0.0"
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallElevationSlow,
                "Demo: Elevated install takes very long",
                "1.0.0",
                "Simulates an elevated installation that runs longer than 10 minutes and asks whether to keep waiting."
            )
        );
        feed.PublishVersions(
            DemoProducts.Scenario(
                DemoIds.InstallDetailThrows,
                "Demo: Detail view install throws",
                "1.0.0",
                "Open the product, select the older version 1.0.0 in the detail view and click Install: loading that version fails with an exception."
            ),
            OldAndNew
        );
        feed.FailVersionLookups(DemoIds.InstallDetailThrows);
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallLockConflict,
                "Demo: Lock conflict on install",
                "1.0.0",
                "Another process holds the operation lock of this product."
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.InstallPluginConfig,
                "Demo: Plugin requires configuration",
                "1.0.0",
                "The plugin needs configuration. Cancel the dialog to see the error.",
                withPlugin: true
            )
        );
        return feed;
    }

    private static DemoFeedContent CreateUpdateFeed()
    {
        DemoFeedContent feed = new DemoFeedContent(
            DemoIds.FeedUpdateScenarios,
            "Demo errors: Update"
        );

        PublishOldAndNew(
            feed,
            DemoIds.UpdatePostInstallFails,
            "Demo: Update fails at PostInstall",
            "PostInstall of the new version fails. A dialog asks to keep the new version or roll back.",
            withPlugin: true
        );
        PublishOldAndNew(
            feed,
            DemoIds.UpdateCancel,
            "Demo: Slow update (cancel it)",
            "Slow update. Cancel it from the installation panel and check that the old version is restored."
        );
        PublishOldAndNew(
            feed,
            DemoIds.UpdateLockedFiles,
            "Demo: Update with locked files",
            "Files of the running product are locked by processes."
        );
        PublishOldAndNew(
            feed,
            DemoIds.UpdateLockConflict,
            "Demo: Lock conflict on update and uninstall",
            "Another process holds the operation lock of this product."
        );
        PublishOldAndNew(
            feed,
            DemoIds.UpdateStale,
            "Demo: Update version changes",
            "Use the demo control window to change what the feed offers after the Updates tab was loaded."
        );
        PublishOldAndNew(
            feed,
            DemoIds.UpdateChecksum,
            "Demo: Update with wrong checksum",
            "The update package does not match its checksum."
        );
        PublishOldAndNew(
            feed,
            DemoIds.DependencyCore,
            "Demo: Core product (update triggers dependents)",
            "Updating this product offers the dependent update of the add-on."
        );
        PublishOldAndNew(
            feed,
            DemoIds.DependencyAddOn,
            "Demo: Add-on (depends on core product)",
            "Requires the core product.",
            requiredProductIds: [DemoIds.DependencyCore]
        );

        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.UninstallLockedFiles,
                "Demo: Uninstall with locked files",
                "1.0.0",
                "Files of the product are locked by processes."
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.UninstallReboot,
                "Demo: Uninstall with reboot hint",
                "1.0.0",
                "The uninstall ends with the reboot hint (simulated, nothing is scheduled in Windows)."
            )
        );
        feed.Publish(
            DemoProducts.Scenario(
                DemoIds.Integrity,
                "Demo: File check and repair",
                "1.0.0",
                "Verify files finds a modified and a missing file. Repair restores them."
            )
        );

        return feed;
    }

    private static void PublishOldAndNew(
        DemoFeedContent feed,
        string productId,
        string title,
        string description,
        bool withPlugin = false,
        string[]? requiredProductIds = null
    ) =>
        feed.PublishVersions(
            DemoProducts.Scenario(
                productId,
                title,
                "1.0.0",
                description,
                withPlugin: withPlugin,
                requiredProductIds: requiredProductIds
            ),
            OldAndNew
        );
}
