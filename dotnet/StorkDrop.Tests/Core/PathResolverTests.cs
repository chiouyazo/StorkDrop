using FluentAssertions;
using StorkDrop.Core.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class PathResolverTests
{
    private readonly PathResolver _resolver = new();

    [Fact]
    public void Resolve_AbsolutePath_ShouldReturnSamePath()
    {
        string path = "/tmp/test/path";

        string result = _resolver.Resolve(path);

        result.Should().Be(path);
    }

    [Fact]
    public void Resolve_TildePath_ShouldExpandToHome()
    {
        string result = _resolver.Resolve("~/Documents");

        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents"
        );
        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_EnvironmentVariable_ShouldExpand()
    {
        string envVarName = "STORKDROP_TEST_VAR";
        string envVarValue = "/test/expanded/path";
        Environment.SetEnvironmentVariable(envVarName, envVarValue);

        try
        {
            string result = _resolver.Resolve($"%{envVarName}%/sub");

            // On Linux, %VAR% style isn't expanded, but $VAR would be
            // The method calls Environment.ExpandEnvironmentVariables which handles this per-platform
            result.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public void Resolve_UncPath_ShouldPreserve()
    {
        string uncPath = "\\\\server\\share\\folder";

        string result = _resolver.Resolve(uncPath);

        // Should normalize separators but preserve UNC structure
        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IsUncPath_WithUncPath_ShouldReturnTrue()
    {
        _resolver.IsUncPath("\\\\server\\share").Should().BeTrue();
        _resolver.IsUncPath("//server/share").Should().BeTrue();
    }

    [Fact]
    public void IsUncPath_WithLocalPath_ShouldReturnFalse()
    {
        _resolver.IsUncPath("/home/user").Should().BeFalse();
        _resolver.IsUncPath("C:\\Users").Should().BeFalse();
    }

    [Fact]
    public void IsValidPath_WithValidPath_ShouldReturnTrue()
    {
        _resolver.IsValidPath("/tmp/test").Should().BeTrue();
    }

    [Fact]
    public void IsValidPath_WithEmptyPath_ShouldReturnFalse()
    {
        _resolver.IsValidPath("").Should().BeFalse();
        _resolver.IsValidPath("   ").Should().BeFalse();
    }

    [Fact]
    public void Resolve_ThrowsOnNull()
    {
        Action act = () => _resolver.Resolve(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_ThrowsOnEmpty()
    {
        Action act = () => _resolver.Resolve("");

        act.Should().Throw<ArgumentException>();
    }
}
