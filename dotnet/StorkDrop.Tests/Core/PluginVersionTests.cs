using FluentAssertions;
using StorkDrop.Contracts;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class PluginVersionTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("v2.5.0")]
    [InlineData("1.2.3-alpha.1")]
    [InlineData("1.0.0+build.5")]
    public void TryParse_ShouldAcceptValidVersions(string version)
    {
        PluginVersion.TryParse(version, out PluginVersion result).Should().BeTrue();
        result.Value.Should().Be(version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1")]
    public void TryParse_ShouldRejectInvalidVersions(string? version)
    {
        PluginVersion.TryParse(version, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_ShouldThrowOnInvalidVersion()
    {
        Action act = () => PluginVersion.Parse("nope");

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("2.5.0", "2.4.9")]
    [InlineData("2.5.1", "2.5.0")]
    [InlineData("1.0.0", "1.0.0-alpha")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    public void GreaterThan_ShouldOrderVersions(string higher, string lower)
    {
        PluginVersion a = PluginVersion.Parse(higher);
        PluginVersion b = PluginVersion.Parse(lower);

        (a > b).Should().BeTrue();
        (a >= b).Should().BeTrue();
        (b < a).Should().BeTrue();
        (b <= a).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("v1.2.0", "1.2")]
    [InlineData("1.0.0+build.1", "1.0.0+build.2")]
    [InlineData("1.0.0-alpha.01", "1.0.0-alpha.1")]
    public void EqualVersions_ShouldCompareEqualAndShareHashCode(string x, string y)
    {
        PluginVersion a = PluginVersion.Parse(x);
        PluginVersion b = PluginVersion.Parse(y);

        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.CompareTo(b).Should().Be(0);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void AppliesFrom_ScenarioReadsNaturally()
    {
        PluginVersion current = PluginVersion.Parse("2.6.0");
        PluginVersion appliesFrom = PluginVersion.Parse("2.5.0");

        (current >= appliesFrom).Should().BeTrue();
    }
}
