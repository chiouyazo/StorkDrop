using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class MarketplaceChannelSelectorTests
{
    private static (ProductManifest Manifest, string FeedId) Channel(
        string feedId,
        string version
    ) =>
        (
            new ProductManifest(
                "acme",
                "Acme",
                version,
                new DateOnly(2026, 1, 1),
                InstallType.Executable
            ),
            feedId
        );

    private static (ProductManifest Manifest, string FeedId) Select(
        string? installedFeedId,
        params (ProductManifest Manifest, string FeedId)[] channels
    ) =>
        MarketplaceChannelSelector.SelectBest(
            channels,
            c => c.Manifest,
            c => c.FeedId,
            installedFeedId
        );

    [Fact]
    public void SelectBest_FeatureBuildWithHigherCoreDoesNotReplaceTheReleaseChannel()
    {
        (ProductManifest Manifest, string FeedId) result = Select(
            null,
            Channel("nexus:prod", "2.36.0"),
            Channel("nexus:epic", "2.37.0-2386-20260915-2438")
        );

        result.FeedId.Should().Be("nexus:prod");
    }

    [Fact]
    public void SelectBest_InstalledProductKeepsItsOwnChannel()
    {
        (ProductManifest Manifest, string FeedId) result = Select(
            "nexus:epic",
            Channel("nexus:prod", "2.36.0"),
            Channel("nexus:epic", "2.36.1-2386-20260915-99")
        );

        result.FeedId.Should().Be("nexus:epic");
        result.Manifest.Version.Should().Be("2.36.1-2386-20260915-99");
    }

    [Fact]
    public void SelectBest_PicksTheHighestVersionInsideTheChosenGroup()
    {
        (ProductManifest Manifest, string FeedId) result = Select(
            null,
            Channel("nexus:prod", "2.36.0"),
            Channel("nexus:preview", "2.36.1"),
            Channel("nexus:epic", "9.0.0-1-20260101-1")
        );

        result.FeedId.Should().Be("nexus:preview");
    }

    [Fact]
    public void SelectBest_WhenAllChannelsArePreReleases_PicksTheHighestBuild()
    {
        (ProductManifest Manifest, string FeedId) result = Select(
            null,
            Channel("nexus:a", "2.36.1-2386-20260915-99"),
            Channel("nexus:b", "2.36.1-2386-20260915-100")
        );

        result.FeedId.Should().Be("nexus:b");
    }

    [Fact]
    public void SelectBest_WhenTheInstalledChannelIsGone_FallsBackToTheReleaseChannel()
    {
        (ProductManifest Manifest, string FeedId) result = Select(
            "nexus:removed",
            Channel("nexus:prod", "2.36.0"),
            Channel("nexus:epic", "2.40.0-1-20260101-1")
        );

        result.FeedId.Should().Be("nexus:prod");
    }
}
