using FluentAssertions;
using NSubstitute;
using StorkDrop.App.Services.Cli;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Cli;

public sealed class CliFeedSelectorTests
{
    private readonly IFeedRegistry _registry = Substitute.For<IFeedRegistry>();

    public CliFeedSelectorTests()
    {
        _registry
            .GetFeeds()
            .Returns([new FeedInfo("prod", "Production"), new FeedInfo("dev", "Dev")]);
    }

    [Fact]
    public void NoToken_ReturnsAllFeeds()
    {
        CliFeedSelector.Select(_registry, null).Should().HaveCount(2);
        CliFeedSelector.Select(_registry, " ").Should().HaveCount(2);
    }

    [Fact]
    public void MatchesById_CaseInsensitive()
    {
        CliFeedSelector
            .Select(_registry, "PROD")
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be("prod");
    }

    [Fact]
    public void MatchesByName_CaseInsensitive()
    {
        CliFeedSelector
            .Select(_registry, "dev")
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be("dev");
        CliFeedSelector
            .Select(_registry, "production")
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be("prod");
    }

    [Fact]
    public void Unknown_ThrowsNotFoundListingConfiguredFeeds()
    {
        Action act = () => CliFeedSelector.Select(_registry, "x");

        CliException ex = act.Should().Throw<CliException>().Which;
        ex.ExitCode.Should().Be(CliExitCodes.NotFound);
        ex.Message.Should().Contain("prod").And.Contain("dev");
    }
}
