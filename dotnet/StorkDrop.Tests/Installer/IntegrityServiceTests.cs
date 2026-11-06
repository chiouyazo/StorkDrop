using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class IntegrityServiceTests
{
    [Fact]
    public async Task Verify_WhenOneFileCannotBeRead_ReportsItAsUnverifiableAndChecksTheRest()
    {
        using EngineTestHarness harness = new EngineTestHarness();
        harness.WriteInstalledFile("ok.dll", "good");
        harness.WriteInstalledFile("locked.dll", "held");
        harness.WriteInstalledFile("changed.dll", "new content");
        string okHash = await FileHasher.ComputeSha256Async(harness.InstalledFile("ok.dll"));
        string lockedHash = await FileHasher.ComputeSha256Async(
            harness.InstalledFile("locked.dll")
        );
        Directory.CreateDirectory(StorkPaths.StorkConfigDir);
        await FileManifestStore.WriteAsync(
            harness.FileManifestFile,
            [
                new TrackedFile("locked.dll", lockedHash, 4),
                new TrackedFile("changed.dll", "0000", 0),
                new TrackedFile("ok.dll", okHash, 4),
                new TrackedFile("gone.dll", "1111", 0),
            ]
        );
        IntegrityService service = new IntegrityService(
            Substitute.For<IFeedRegistry>(),
            NullLogger<IntegrityService>.Instance
        );

        using FileStream hold = new FileStream(
            harness.InstalledFile("locked.dll"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );
        IntegrityReport report = await service.VerifyAsync(harness.Installed);

        report
            .Files.ToDictionary(f => f.Path, f => f.Status)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, FileIntegrityStatus>
                {
                    ["locked.dll"] = FileIntegrityStatus.Unverifiable,
                    ["changed.dll"] = FileIntegrityStatus.Modified,
                    ["ok.dll"] = FileIntegrityStatus.Ok,
                    ["gone.dll"] = FileIntegrityStatus.Missing,
                }
            );
    }
}
