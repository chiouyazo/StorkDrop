using FluentAssertions;
using StorkDrop.Contracts.Services;
using StorkDrop.Core.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class VersionComparerTests
{
    private readonly VersionComparer _comparer = VersionComparer.Instance;

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.1.0", "1.0.0", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "2.0.0", -1)]
    public void Compare_ShouldHandleBasicVersionComparison(string x, string y, int expected)
    {
        int result = _comparer.Compare(x, y);

        Math.Sign(result).Should().Be(expected);
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0", 0)]
    [InlineData("V2.1.0", "2.1.0", 0)]
    [InlineData("v1.0.0", "v1.0.0", 0)]
    public void Compare_ShouldIgnoreLeadingV(string x, string y, int expected)
    {
        int result = _comparer.Compare(x, y);

        Math.Sign(result).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-beta", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-alpha", 1)]
    public void Compare_ShouldHandlePreReleaseVersions(string x, string y, int expected)
    {
        int result = _comparer.Compare(x, y);

        Math.Sign(result).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0", 0)]
    public void Compare_ShouldHandleDifferentPartCounts(string x, string y, int expected)
    {
        int result = _comparer.Compare(x, y);

        Math.Sign(result).Should().Be(expected);
    }

    [Fact]
    public void Compare_NullValues_ShouldHandleGracefully()
    {
        _comparer.Compare(null, null).Should().Be(0);
        _comparer.Compare(null, "1.0.0").Should().BeLessThan(0);
        _comparer.Compare("1.0.0", null).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("2.0.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "2.0.0", false)]
    [InlineData("1.2.3", "1.2.2", true)]
    public void IsNewer_ShouldReturnCorrectResult(string candidate, string baseline, bool expected)
    {
        VersionComparer.IsNewer(candidate, baseline).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.0", true)]
    [InlineData("v1.0.0", true)]
    [InlineData("1.0.0-alpha", true)]
    [InlineData("10.20.30", true)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("1", false)]
    [InlineData("...", false)]
    [InlineData("1..0", false)]
    public void IsValid_ShouldValidateCorrectly(string version, bool expected)
    {
        VersionComparer.IsValid(version).Should().Be(expected);
    }

    [Fact]
    public void Compare_SameReference_ShouldReturnZero()
    {
        string version = "1.0.0";
        _comparer.Compare(version, version).Should().Be(0);
    }

    [Theory]
    [InlineData("10.0.0", "9.9.9", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.10", "1.0.9", 1)]
    public void Compare_ShouldHandleMultiDigitParts(string x, string y, int expected)
    {
        Math.Sign(_comparer.Compare(x, y)).Should().Be(expected);
    }
}
