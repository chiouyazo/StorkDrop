using FluentAssertions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class ApprovedUpdateResolverTests
{
    private static ProductManifest Manifest(string version) =>
        new ProductManifest(
            "acme",
            "Acme",
            version,
            new DateOnly(2026, 1, 1),
            InstallType.Executable
        );

    [Fact]
    public async Task Resolve_WhenApprovedVersionStillServed_ReturnsExactlyThatVersion()
    {
        IRegistryClient client = Substitute.For<IRegistryClient>();
        client
            .GetProductManifestAsync(
                "acme",
                "2.36.1-2386-20260915-2438",
                Arg.Any<CancellationToken>()
            )
            .Returns(Manifest("2.36.1-2386-20260915-2438"));
        client
            .GetProductManifestAsync("acme", Arg.Any<CancellationToken>())
            .Returns(Manifest("2.36.1-2386-20260915-2439"));

        ApprovedUpdateResolution result = await ApprovedUpdateResolver.ResolveAsync(
            client,
            "acme",
            "2.36.1-2386-20260915-2438",
            CancellationToken.None
        );

        result.Approved.Should().NotBeNull();
        result.Approved!.Version.Should().Be("2.36.1-2386-20260915-2438");
        result.CurrentLatest.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_WhenApprovedVersionIsGone_ReturnsCurrentLatestAndNoManifestToInstall()
    {
        IRegistryClient client = Substitute.For<IRegistryClient>();
        client
            .GetProductManifestAsync("acme", "2.0.0", Arg.Any<CancellationToken>())
            .Returns((ProductManifest?)null);
        client
            .GetProductManifestAsync("acme", Arg.Any<CancellationToken>())
            .Returns(Manifest("2.1.0"));

        ApprovedUpdateResolution result = await ApprovedUpdateResolver.ResolveAsync(
            client,
            "acme",
            "2.0.0",
            CancellationToken.None
        );

        result.Approved.Should().BeNull();
        result.CurrentLatest!.Version.Should().Be("2.1.0");
    }

    [Fact]
    public async Task Resolve_WhenClientIgnoresTheVersionParameter_DoesNotInstallADifferentVersion()
    {
        IRegistryClient client = Substitute.For<IRegistryClient>();
        client
            .GetProductManifestAsync("acme", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Manifest("2.1.0"));
        client
            .GetProductManifestAsync("acme", Arg.Any<CancellationToken>())
            .Returns(Manifest("2.1.0"));

        ApprovedUpdateResolution result = await ApprovedUpdateResolver.ResolveAsync(
            client,
            "acme",
            "2.0.0",
            CancellationToken.None
        );

        result.Approved.Should().BeNull();
        result.CurrentLatest!.Version.Should().Be("2.1.0");
    }
}
