using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Publisher;
using Xunit;

namespace StorkDrop.Tests.Publisher;

public sealed class LatestManifestPolicyTests
{
    private static ProductManifest Manifest(string version) =>
        new ProductManifest(
            ProductId: "acme.app",
            Title: "Acme",
            Version: version,
            ReleaseDate: new DateOnly(2026, 1, 1),
            InstallType: InstallType.Suite,
            Description: "d"
        );

    [Fact]
    public void ShouldReplace_NoCurrentLatest_ReturnsTrue()
    {
        LatestManifestPolicy.ShouldReplace(null, Manifest("1.0.0"), false).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.0-5", "1.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    public void ShouldReplace_PublishedNotLower_ReturnsTrue(string current, string published)
    {
        LatestManifestPolicy
            .ShouldReplace(Manifest(current), Manifest(published), false)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.0.0", "1.0.0-5")]
    public void ShouldReplace_PublishedLower_ReturnsFalse(string current, string published)
    {
        LatestManifestPolicy
            .ShouldReplace(Manifest(current), Manifest(published), false)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShouldReplace_PublishedLowerButForced_ReturnsTrue()
    {
        LatestManifestPolicy
            .ShouldReplace(Manifest("2.0.0"), Manifest("1.0.0"), true)
            .Should()
            .BeTrue();
    }
}
