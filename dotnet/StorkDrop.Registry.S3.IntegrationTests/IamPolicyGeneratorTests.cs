using System.Text.Json;
using FluentAssertions;
using StorkDrop.Publisher;
using StorkDrop.Registry.S3;
using Xunit;

namespace StorkDrop.Registry.S3.IntegrationTests;

/// <summary>
/// The IAM policy generator and the key-name validator have no external dependency, so these run
/// without a container. They pin the least-privilege shape (read-only, one channel prefix, no catalog)
/// and the key-segment safety rules.
/// </summary>
public sealed class IamPolicyGeneratorTests
{
    [Fact]
    public void Grant_is_read_only_and_scoped_to_the_channel_prefix()
    {
        string json = IamPolicyGenerator.ForCustomer("acme-bucket", prefix: null, channel: "prod");

        json.Should().Contain("arn:aws:s3:::acme-bucket/prod/*");
        json.Should().NotContain("dev");
        json.Should().NotContain("feature");
        json.Should().NotContain("catalog");

        json.Should().Contain("s3:GetObject");
        json.Should().Contain("s3:ListBucket");
        json.Should().NotContain("s3:PutObject");
        json.Should().NotContain("s3:DeleteObject");
        json.Should().Contain("s3:prefix");

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Statement").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Prefix_is_applied_to_the_channel_path()
    {
        string json = IamPolicyGenerator.ForCustomer(
            "acme-bucket",
            prefix: "tenant-1",
            channel: "prod"
        );

        json.Should().Contain("arn:aws:s3:::acme-bucket/tenant-1/prod/*");
    }

    [Fact]
    public void Emitted_document_is_valid_json()
    {
        string json = IamPolicyGenerator.ForCustomer("acme-bucket");
        Action parse = () => JsonDocument.Parse(json);
        parse.Should().NotThrow();
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("dev")]
    [InlineData("acme.app")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-rc.1")]
    public void S3Names_accepts_safe_segments(string name)
    {
        S3Names.IsValidSegment(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData("*")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    public void S3Names_rejects_unsafe_segments(string name)
    {
        S3Names.IsValidSegment(name).Should().BeFalse();
        Action require = () => S3Names.Require(name, "segment");
        require.Should().Throw<ArgumentException>();
    }
}
