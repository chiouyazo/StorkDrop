using FluentAssertions;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class ProductPathTokenTests
{
    [Theory]
    [InlineData("{ProductPath:acme.suite}/addons/plugins", "acme.suite")]
    [InlineData("{ProductPath:acme.app}", "acme.app")]
    [InlineData("C:/base/{ProductPath:foo}/sub", "foo")]
    [InlineData("{ProductPath: spaced-id }/x", "spaced-id")]
    public void GetReferencedProductId_extracts_the_id(string path, string expected)
    {
        ProductPathToken.GetReferencedProductId(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:/Program Files/Acme/{InstanceId}")]
    [InlineData("no token here")]
    [InlineData("{ProductPath:}")]
    public void GetReferencedProductId_returns_null_without_a_token(string? path)
    {
        ProductPathToken.GetReferencedProductId(path).Should().BeNull();
    }

    [Fact]
    public void Resolve_substitutes_the_instance_path()
    {
        string result = ProductPathToken.Resolve(
            "{ProductPath:acme.suite}/addons/plugins",
            "C:/Program Files/Acme/Suite/instance-1"
        );

        result.Should().Be("C:/Program Files/Acme/Suite/instance-1/addons/plugins");
    }

    [Theory]
    [InlineData("C:/base/inst/")]
    [InlineData("C:/base/inst\\")]
    public void Resolve_trims_trailing_separators_on_the_instance_path(string instancePath)
    {
        ProductPathToken
            .Resolve("{ProductPath:x}/plugins", instancePath)
            .Should()
            .Be("C:/base/inst/plugins");
    }

    [Fact]
    public void Resolve_keeps_a_dollar_sign_in_the_instance_path_literal()
    {
        ProductPathToken.Resolve("{ProductPath:x}/p", "C:/a$b").Should().Be("C:/a$b/p");
    }
}
