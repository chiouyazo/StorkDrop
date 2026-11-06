using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class UpdateDetectorTests
{
    private static ProductManifest Manifest(string productId, string version) =>
        new ProductManifest(
            productId,
            productId,
            version,
            new DateOnly(2026, 1, 1),
            InstallType.Executable
        );

    private static InstalledProduct Installed(
        string productId,
        string version,
        string? feedId,
        string instanceId = "default"
    ) =>
        new InstalledProduct(
            productId,
            instanceId,
            productId,
            version,
            @"C:\" + productId + instanceId,
            DateTime.UtcNow,
            feedId
        );

    private static Dictionary<string, IReadOnlyList<ProductManifest>> Feeds(
        params (string FeedId, ProductManifest[] Products)[] feeds
    ) => feeds.ToDictionary(f => f.FeedId, f => (IReadOnlyList<ProductManifest>)f.Products);

    [Fact]
    public void FindUpdates_IgnoresNewerVersionsThatAreOnlyOfferedOnAnotherChannel()
    {
        InstalledProduct installed = Installed("acme", "2.36.0", "nexus:prod");
        Dictionary<string, IReadOnlyList<ProductManifest>> feeds = Feeds(
            ("nexus:prod", [Manifest("acme", "2.36.0")]),
            ("nexus:epic", [Manifest("acme", "2.37.0-2386-20260915-2438")])
        );

        UpdateDetector.FindUpdates([installed], feeds).Should().BeEmpty();
    }

    [Fact]
    public void FindUpdates_ReportsTheUpdateOfTheInstallationFeed()
    {
        InstalledProduct installed = Installed("acme", "2.36.0", "nexus:prod");
        Dictionary<string, IReadOnlyList<ProductManifest>> feeds = Feeds(
            ("nexus:prod", [Manifest("acme", "2.36.1")]),
            ("nexus:epic", [Manifest("acme", "2.40.0-1-20260101-1")])
        );

        IReadOnlyList<AvailableUpdate> updates = UpdateDetector.FindUpdates([installed], feeds);

        updates.Should().ContainSingle().Which.Latest.Version.Should().Be("2.36.1");
    }

    [Fact]
    public void FindUpdates_EvaluatesEveryInstanceSeparately()
    {
        InstalledProduct stable = Installed("acme", "1.0.0", "nexus:prod", "default");
        InstalledProduct epic = Installed("acme", "2.0.0-5-20260101-9", "nexus:epic", "second");
        Dictionary<string, IReadOnlyList<ProductManifest>> feeds = Feeds(
            ("nexus:prod", [Manifest("acme", "1.1.0")]),
            ("nexus:epic", [Manifest("acme", "2.0.0-5-20260101-100")])
        );

        IReadOnlyList<AvailableUpdate> updates = UpdateDetector.FindUpdates([stable, epic], feeds);

        updates
            .Select(u => (u.Installed.InstanceId, u.Latest.Version))
            .Should()
            .BeEquivalentTo([("default", "1.1.0"), ("second", "2.0.0-5-20260101-100")]);
    }

    [Fact]
    public void FindUpdates_SkipsInstancesWhoseFeedWasNotReadOrIsUnknown()
    {
        InstalledProduct unreadable = Installed("acme", "1.0.0", "nexus:prod");
        InstalledProduct noFeed = Installed("other", "1.0.0", null);
        Dictionary<string, IReadOnlyList<ProductManifest>> feeds = Feeds(
            ("nexus:epic", [Manifest("acme", "9.0.0"), Manifest("other", "9.0.0")])
        );

        UpdateDetector.FindUpdates([unreadable, noFeed], feeds).Should().BeEmpty();
    }

    [Fact]
    public void FindUpdates_RecognizesTheHigherFeatureBuildNumber()
    {
        InstalledProduct installed = Installed("acme", "2.36.1-2386-20260915-99", "nexus:epic");
        Dictionary<string, IReadOnlyList<ProductManifest>> feeds = Feeds(
            ("nexus:epic", [Manifest("acme", "2.36.1-2386-20260915-100")])
        );

        UpdateDetector.FindUpdates([installed], feeds).Should().ContainSingle();
    }
}
