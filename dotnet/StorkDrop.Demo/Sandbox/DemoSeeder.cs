using System.IO;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Demo.Scenarios;
using StorkDrop.Demo.Services;

namespace StorkDrop.Demo.Sandbox;

internal static class DemoSeeder
{
    public static async Task SeedAsync(
        DemoFeedRegistry registry,
        IProductRepository repository,
        DemoLockState locks
    )
    {
        foreach (DemoSeed seed in DemoCatalog.CreateSeeds())
        {
            ProductManifest? manifest = registry
                .Feed(seed.FeedId)
                ?.Find(seed.ProductId, seed.Version);
            if (manifest is null)
                continue;

            string installPath = DemoSandbox.Contain(
                (manifest.RecommendedInstallPath ?? seed.ProductId).Replace(
                    "{DemoPath}",
                    DemoSandbox.AppsRoot
                )
            );
            string uniqueId = InstanceIdHelper.GenerateUniqueId();
            List<TrackedFile> tracked = await DemoPackage.WriteAsync(
                manifest,
                installPath,
                null,
                TimeSpan.Zero,
                CancellationToken.None
            );
            await DemoPackage.WriteFileManifestAsync(
                manifest.ProductId,
                uniqueId,
                tracked,
                CancellationToken.None
            );

            await repository.AddAsync(
                new InstalledProduct(
                    manifest.ProductId,
                    seed.InstanceId,
                    manifest.Title,
                    manifest.Version,
                    installPath,
                    DateTime.UtcNow.AddDays(-12),
                    seed.FeedId,
                    InstallType: manifest.InstallType,
                    BadgeText: manifest.BadgeText,
                    BadgeColor: manifest.BadgeColor,
                    InstanceUniqueId: uniqueId
                )
            );

            if (seed.ProductId is DemoIds.UpdateLockedFiles or DemoIds.UninstallLockedFiles)
                locks.Arm(installPath, DemoPackage.MainFile(manifest));

            if (seed.ProductId == DemoIds.Integrity)
                Tamper(installPath);
        }
    }

    public static void Tamper(string installPath)
    {
        string modified = Path.Combine(installPath, "lib", "core.dll");
        if (File.Exists(modified))
            File.AppendAllText(modified, "tampered");

        string missing = Path.Combine(installPath, "config", "default.json");
        if (File.Exists(missing))
            File.Delete(missing);
    }
}
