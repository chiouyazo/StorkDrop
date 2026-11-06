using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class ElevationConfigFileTests
{
    [Fact]
    public async Task WriteAsync_RoundTripsValues_AndReadAndDeleteRemovesTheFile()
    {
        using TestDirectory dir = new TestDirectory();
        Dictionary<string, string> values = new Dictionary<string, string>
        {
            ["Password"] = "p@ss\"word",
            ["__instanceUniqueId"] = "abc12345",
        };

        string path = await ElevationConfigFile.WriteAsync(
            values,
            dir.Path,
            CancellationToken.None
        );

        File.Exists(path).Should().BeTrue();
        ElevationConfigFile.ReadAndDelete(path).Should().BeEquivalentTo(values);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_RestrictsAccessToCurrentUserAdministratorsAndSystem()
    {
        using TestDirectory dir = new TestDirectory();
        string path = await ElevationConfigFile.WriteAsync(
            new Dictionary<string, string> { ["k"] = "v" },
            dir.Path,
            CancellationToken.None
        );
        try
        {
            FileSecurity security = new FileInfo(path).GetAccessControl();

            security.AreAccessRulesProtected.Should().BeTrue();
            List<SecurityIdentifier> allowed = security
                .GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(rule => (SecurityIdentifier)rule.IdentityReference)
                .ToList();
            allowed.Should().Contain(WindowsIdentity.GetCurrent().User!);
            allowed
                .Should()
                .Contain(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            allowed
                .Should()
                .NotContain(new SecurityIdentifier(WellKnownSidType.WorldSid, null))
                .And.NotContain(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null))
                .And.NotContain(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)
                );
        }
        finally
        {
            ElevationConfigFile.TryDelete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesAUniqueFilePerCall()
    {
        using TestDirectory dir = new TestDirectory();
        string first = await ElevationConfigFile.WriteAsync(
            new Dictionary<string, string>(),
            dir.Path,
            CancellationToken.None
        );
        string second = await ElevationConfigFile.WriteAsync(
            new Dictionary<string, string>(),
            dir.Path,
            CancellationToken.None
        );
        try
        {
            first.Should().NotBe(second);
            Path.GetDirectoryName(first).Should().Be(dir.Path);
        }
        finally
        {
            ElevationConfigFile.TryDelete(first);
            ElevationConfigFile.TryDelete(second);
        }
    }

    [Fact]
    public async Task WriteAsync_WithCancelledToken_LeavesNoFileBehind()
    {
        using TestDirectory dir = new TestDirectory();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () =>
            ElevationConfigFile.WriteAsync(new Dictionary<string, string>(), dir.Path, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Fact]
    public void ReadAndDelete_DeletesTheFileEvenWhenItIsNotValidJson()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("elevation-config-x.json");
        File.WriteAllText(path, "{ not json");

        Action act = () => ElevationConfigFile.ReadAndDelete(path);

        act.Should().Throw<System.Text.Json.JsonException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void ReadAndDelete_ReturnsNullForAMissingFile()
    {
        using TestDirectory dir = new TestDirectory();

        ElevationConfigFile.ReadAndDelete(dir.File("missing.json")).Should().BeNull();
    }

    [Fact]
    public void CleanupStale_RemovesOnlyOldElevationFiles()
    {
        using TestDirectory dir = new TestDirectory();
        string oldConfig = dir.File("elevation-config-old.json");
        string oldProgress = dir.File("elevation-progress-old.log");
        string freshConfig = dir.File("elevation-config-fresh.json");
        string unrelated = dir.File("other.json");
        foreach (string file in new[] { oldConfig, oldProgress, freshConfig, unrelated })
            File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(oldConfig, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(oldProgress, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));

        ElevationConfigFile.CleanupStale(dir.Path, TimeSpan.FromHours(4));

        File.Exists(oldConfig).Should().BeFalse();
        File.Exists(oldProgress).Should().BeFalse();
        File.Exists(freshConfig).Should().BeTrue();
        File.Exists(unrelated).Should().BeTrue();
    }

    [Fact]
    public void CleanupStale_ToleratesAMissingDirectory()
    {
        Action act = () =>
            ElevationConfigFile.CleanupStale(
                Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid()),
                TimeSpan.FromHours(1)
            );

        act.Should().NotThrow();
    }
}
