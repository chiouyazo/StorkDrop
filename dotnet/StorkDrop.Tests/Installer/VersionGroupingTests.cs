using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class VersionGroupingTests
{
    private static readonly string[] EpicVersions =
    [
        "2.35.0-2401-20261001-0900",
        "2.34.7-2390-20260920-2455",
        "2.34.7-2386-20260915-2438",
        "2.34.7-2386-20260915-2401",
        "2.34.7-2386-20260901-2210",
    ];

    [Fact]
    public void Build_ReturnsNull_ForFlatSemVerVersions()
    {
        VersionGrouping.Build(["2.0.0", "1.9.2", "1.9.1"]).Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_WhenSegmentCountsDiffer()
    {
        VersionGrouping.Build(["2.0.0", "2.34.7-2386-20260915-2438"]).Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_ForSingleVersion()
    {
        VersionGrouping.Build(["2.34.7-2386-20260915-2438"]).Should().BeNull();
    }

    [Fact]
    public void Build_GroupsRegularCompoundVersions_NewestFirst()
    {
        VersionTree? tree = VersionGrouping.Build(EpicVersions);

        tree.Should().NotBeNull();
        tree!.Levels.Should().BeNull();
        tree.Roots.Select(r => r.Segment).Should().ContainInOrder("2.35.0", "2.34.7");

        VersionNode core = tree.Roots.First(r => r.Segment == "2.34.7");
        core.FullVersion.Should().BeNull();
        core.Children.Select(c => c.Segment).Should().ContainInOrder("2390", "2386");

        VersionNode build2386 = core.Children.First(c => c.Segment == "2386");
        VersionNode date = build2386.Children.First(c => c.Segment == "20260915");
        date.Children.Select(c => c.Segment).Should().ContainInOrder("2438", "2401");
        date.Children.First(c => c.Segment == "2438")
            .FullVersion.Should()
            .Be("2.34.7-2386-20260915-2438");
    }

    [Fact]
    public void Build_AppliesSchemaLabelsAndDateFormatting()
    {
        VersionSchema schema = new VersionSchema(
            "-",
            [
                new VersionPart("Release"),
                new VersionPart("Build"),
                new VersionPart("Date", "date:yyyyMMdd"),
                new VersionPart("Revision"),
            ]
        );

        VersionTree? tree = VersionGrouping.Build(EpicVersions, schema);

        tree.Should().NotBeNull();
        tree!.Levels.Should().ContainInOrder("Release", "Build", "Date", "Revision");

        VersionNode date = tree
            .Roots.First(r => r.Segment == "2.34.7")
            .Children.First(c => c.Segment == "2386")
            .Children.First(c => c.Segment == "20260915");
        date.Display.Should().Be("2026-09-15");
        date.Segment.Should().Be("20260915");
    }

    [Fact]
    public void Build_FallsBackToGenericGrouping_WhenSchemaDateUnparseable()
    {
        // Third segment is not a yyyyMMdd date -> schema dropped, but segments are still regular.
        string[] versions = ["2.34.7-2386-notadate-2438", "2.34.7-2386-notadate-2401"];
        VersionSchema schema = new VersionSchema(
            "-",
            [
                new VersionPart("Release"),
                new VersionPart("Build"),
                new VersionPart("Date", "date:yyyyMMdd"),
                new VersionPart("Revision"),
            ]
        );

        VersionTree? tree = VersionGrouping.Build(versions, schema);

        tree.Should().NotBeNull();
        tree!.Levels.Should().BeNull();
        tree.Roots.Should().ContainSingle(r => r.Segment == "2.34.7");
    }

    [Fact]
    public void Build_GroupsTwoSegmentPreReleaseVersions()
    {
        VersionTree? tree = VersionGrouping.Build([
            "2.2.0-beta.1",
            "2.1.0-beta.3",
            "2.1.0-beta.2",
            "2.1.0-beta.1",
        ]);

        tree.Should().NotBeNull();
        tree!.Roots.Select(r => r.Segment).Should().ContainInOrder("2.2.0", "2.1.0");
        tree.Roots.First(r => r.Segment == "2.1.0")
            .Children.Select(c => c.FullVersion)
            .Should()
            .Contain("2.1.0-beta.3");
    }
}
