using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class FeedLockTests
{
    private static FeedConfiguration Feed(string id, string? lockHash = null) =>
        new FeedConfiguration(
            Id: id,
            Name: id,
            Url: "https://example.test",
            Repository: null,
            Username: null,
            EncryptedPassword: null,
            PluginId: null,
            LockPasswordHash: lockHash
        );

    [Fact]
    public void Hash_Then_Verify_ShouldRoundTrip()
    {
        string hash = PasswordHasher.Hash("s3cret");

        PasswordHasher.Verify("s3cret", hash).Should().BeTrue();
        PasswordHasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ShouldBeSalted_ProducingDifferentHashesForSamePassword()
    {
        string a = PasswordHasher.Hash("same");
        string b = PasswordHasher.Hash("same");

        a.Should().NotBe(b);
        PasswordHasher.Verify("same", a).Should().BeTrue();
        PasswordHasher.Verify("same", b).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("dG9vLXNob3J0")] // valid base64 but wrong length
    public void Verify_ShouldReturnFalse_ForMissingOrMalformedHash(string? hash)
    {
        PasswordHasher.Verify("anything", hash).Should().BeFalse();
    }

    [Fact]
    public void ResolveFeed_ShouldMatch_ExactId()
    {
        FeedConfiguration[] feeds = [Feed("nexus"), Feed("other")];

        FeedLock.ResolveFeed(feeds, "nexus")!.Id.Should().Be("nexus");
    }

    [Fact]
    public void ResolveFeed_ShouldMatch_CompositeDiscoveryId_ToBaseFeed()
    {
        // Discovery mode expands "nexus" into runtime ids like "nexus:raw-hosted".
        FeedConfiguration[] feeds = [Feed("nexus"), Feed("other")];

        FeedLock.ResolveFeed(feeds, "nexus:raw-hosted")!.Id.Should().Be("nexus");
    }

    [Fact]
    public void ResolveFeed_ShouldReturnNull_ForUnknownOrEmpty()
    {
        FeedConfiguration[] feeds = [Feed("nexus")];

        FeedLock.ResolveFeed(feeds, "missing").Should().BeNull();
        FeedLock.ResolveFeed(feeds, null).Should().BeNull();
        FeedLock.ResolveFeed(feeds, "").Should().BeNull();
    }

    [Fact]
    public void IsLocked_ShouldReflectLockHashPresence()
    {
        FeedLock.IsLocked(Feed("a")).Should().BeFalse();
        FeedLock.IsLocked(Feed("a", PasswordHasher.Hash("pw"))).Should().BeTrue();
    }

    [Fact]
    public void IsLocked_ByFeedId_ShouldResolveCompositeThenCheckHash()
    {
        FeedConfiguration[] feeds = [Feed("nexus", PasswordHasher.Hash("pw")), Feed("open")];

        FeedLock.IsLocked(feeds, "nexus:raw-hosted").Should().BeTrue();
        FeedLock.IsLocked(feeds, "open").Should().BeFalse();
        FeedLock.IsLocked(feeds, "unknown").Should().BeFalse();
    }

    [Fact]
    public void Verify_OnFeed_ShouldValidateAgainstStoredHash()
    {
        FeedConfiguration feed = Feed("nexus", PasswordHasher.Hash("open-sesame"));

        FeedLock.Verify(feed, "open-sesame").Should().BeTrue();
        FeedLock.Verify(feed, "nope").Should().BeFalse();
    }
}
