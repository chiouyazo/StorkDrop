using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class UpdatePlannerTests
{
    private static ProductManifest Manifest(string version) =>
        new ProductManifest(
            "acme",
            "Acme",
            version,
            new DateOnly(2026, 1, 1),
            InstallType.Executable
        );

    private static InstalledProduct Installed(string version, string? feedId = "nexus:prod") =>
        new InstalledProduct(
            "acme",
            "second",
            "Acme",
            version,
            @"C:\acme",
            DateTime.UtcNow,
            feedId
        );

    private static IRegistryClient Client(
        string? latest,
        params (string Version, bool Exists)[] versions
    )
    {
        IRegistryClient client = Substitute.For<IRegistryClient>();
        client
            .GetProductManifestAsync("acme", Arg.Any<CancellationToken>())
            .Returns(latest is null ? null : Manifest(latest));
        foreach ((string version, bool exists) in versions)
            client
                .GetProductManifestAsync("acme", version, Arg.Any<CancellationToken>())
                .Returns(exists ? Manifest(version) : null);
        return client;
    }

    private static IFeedRegistry Registry(params (string FeedId, IRegistryClient Client)[] feeds)
    {
        IFeedRegistry registry = Substitute.For<IFeedRegistry>();
        registry.GetFeeds().Returns(feeds.Select(f => new FeedInfo(f.FeedId, f.FeedId)).ToList());
        foreach ((string feedId, IRegistryClient client) in feeds)
            registry.GetClient(feedId).Returns(client);
        return registry;
    }

    [Fact]
    public async Task Plan_UsesTheInstallationFeedAndNeverAnotherChannel()
    {
        IRegistryClient prod = Client("2.36.1");
        IRegistryClient epic = Client("2.40.0-1-20260101-1");
        IFeedRegistry registry = Registry(("nexus:epic", epic), ("nexus:prod", prod));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            null,
            null,
            false
        );

        plan.Decision.Should().Be(UpdateDecision.Proceed);
        plan.FeedId.Should().Be("nexus:prod");
        plan.Manifest!.Version.Should().Be("2.36.1");
        await epic.DidNotReceiveWithAnyArgs().GetProductManifestAsync(default!, default);
    }

    [Fact]
    public async Task Plan_WhenTheInstallationFeedFails_SurfacesTheErrorInsteadOfTryingAnotherFeed()
    {
        IRegistryClient prod = Substitute.For<IRegistryClient>();
        prod.GetProductManifestAsync("acme", Arg.Any<CancellationToken>())
            .Returns<ProductManifest?>(_ => throw new HttpRequestException("401"));
        IRegistryClient epic = Client("9.0.0");
        IFeedRegistry registry = Registry(("nexus:epic", epic), ("nexus:prod", prod));

        Func<Task> plan = () =>
            UpdatePlanner.PlanAsync(registry, Installed("2.36.0"), null, null, false);

        await plan.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Plan_SwitchesChannelOnlyWhenAFeedIsRequestedExplicitly()
    {
        IRegistryClient prod = Client("2.36.1");
        IRegistryClient epic = Client("2.40.0-1-20260101-1");
        IFeedRegistry registry = Registry(("nexus:prod", prod), ("nexus:epic", epic));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            null,
            "nexus:epic",
            false
        );

        plan.Decision.Should().Be(UpdateDecision.Proceed);
        plan.FeedId.Should().Be("nexus:epic");
    }

    [Fact]
    public async Task Plan_WithoutNewerVersion_ReportsUpToDateInsteadOfReinstalling()
    {
        IFeedRegistry registry = Registry(("nexus:prod", Client("2.36.0")));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            null,
            null,
            false
        );

        plan.Decision.Should().Be(UpdateDecision.AlreadyUpToDate);
    }

    [Fact]
    public async Task Plan_WhenTheFeedLatestIsOlderThanInstalled_DoesNotDowngrade()
    {
        IFeedRegistry registry = Registry(("nexus:prod", Client("2.36.1-2386-20260915-99")));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.1-2386-20260915-100"),
            null,
            null,
            false
        );

        plan.Decision.Should().Be(UpdateDecision.AlreadyUpToDate);
    }

    [Fact]
    public async Task Plan_ExplicitOlderVersion_IsBlockedUnlessDowngradeIsAllowed()
    {
        IFeedRegistry registry = Registry(("nexus:prod", Client("2.36.1", ("2.35.0", true))));

        UpdatePlan blocked = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            "2.35.0",
            null,
            false
        );
        UpdatePlan allowed = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            "2.35.0",
            null,
            true
        );

        blocked.Decision.Should().Be(UpdateDecision.DowngradeBlocked);
        allowed.Decision.Should().Be(UpdateDecision.Proceed);
    }

    [Fact]
    public async Task Plan_ExplicitNewerVersion_Proceeds()
    {
        IFeedRegistry registry = Registry(("nexus:prod", Client("2.36.1", ("2.36.1", true))));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            "2.36.1",
            null,
            false
        );

        plan.Decision.Should().Be(UpdateDecision.Proceed);
    }

    [Fact]
    public async Task Plan_UnknownVersion_ReportsManifestNotFound()
    {
        IFeedRegistry registry = Registry(("nexus:prod", Client("2.36.1", ("9.9.9", false))));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0"),
            "9.9.9",
            null,
            false
        );

        plan.Decision.Should().Be(UpdateDecision.ManifestNotFound);
    }

    [Fact]
    public async Task Plan_ForLegacyInstallWithoutFeed_FallsBackToTheFirstFeedThatHasTheProduct()
    {
        IFeedRegistry registry = Registry(("nexus:a", Client(null)), ("nexus:b", Client("2.36.1")));

        UpdatePlan plan = await UpdatePlanner.PlanAsync(
            registry,
            Installed("2.36.0", feedId: null),
            null,
            null,
            false
        );

        plan.Decision.Should().Be(UpdateDecision.Proceed);
        plan.FeedId.Should().Be("nexus:b");
    }
}
