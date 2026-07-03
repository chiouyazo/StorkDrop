using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class FeedReportServiceTests
{
    private static FeedConfiguration Feed(string id) =>
        new FeedConfiguration(id, id, "https://example.test", null, null, null, null);

    private static InstalledProduct Product(string id, string? feedId) =>
        new InstalledProduct(
            ProductId: id,
            InstanceId: "default",
            Title: id.ToUpperInvariant(),
            Version: "1.0.0",
            InstalledPath: $@"C:\{id}",
            InstalledDate: DateTime.UnixEpoch,
            FeedId: feedId
        );

    [Fact]
    public void Sign_MatchesIndependentHmac()
    {
        byte[] body = Encoding.UTF8.GetBytes("hello world");

        string signature = FeedReportService.Sign(body, "s3cret");

        using HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes("s3cret"));
        string expected =
            "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();

        signature.Should().Be(expected);
    }

    [Fact]
    public void Sign_ReturnsEmpty_WhenNoSecret()
    {
        FeedReportService.Sign(Encoding.UTF8.GetBytes("x"), string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void EncodeCloudEvent_ProducesConformantEnvelopeWithCamelCaseData()
    {
        FeedReport report = new FeedReport(
            MachineId: "machine-1",
            Hostname: "host",
            OperatingSystem: "os",
            StorkDropVersion: "1.2.3",
            SentAt: DateTimeOffset.UnixEpoch,
            FeedId: "nexus",
            FeedName: "Nexus",
            CustomerId: "cust-9",
            Products:
            [
                new FeedReportProduct(
                    "p1",
                    "P1",
                    "1.0.0",
                    "nexus:raw",
                    "default",
                    DateTime.UnixEpoch
                ),
            ]
        );

        byte[] encoded = FeedReportService.EncodeCloudEvent(report, "nexus");

        using JsonDocument doc = JsonDocument.Parse(encoded);
        JsonElement root = doc.RootElement;

        root.GetProperty("specversion").GetString().Should().Be("1.0");
        root.GetProperty("type").GetString().Should().Be("com.storkdrop.inventory.report");
        root.GetProperty("subject").GetString().Should().Be("nexus");
        root.GetProperty("source").GetString().Should().Contain("machine-1");

        JsonElement data = root.GetProperty("data");
        data.GetProperty("machineId").GetString().Should().Be("machine-1");
        data.GetProperty("customerId").GetString().Should().Be("cust-9");

        JsonElement product = data.GetProperty("products")[0];
        product.GetProperty("productId").GetString().Should().Be("p1");
        product.GetProperty("channel").GetString().Should().Be("nexus:raw");
    }

    [Fact]
    public void SelectFeedProducts_IncludesOnlyThatFeed_ResolvingCompositeIds()
    {
        FeedConfiguration[] feeds = [Feed("nexus"), Feed("other")];
        List<InstalledProduct> installed =
        [
            Product("p1", "nexus:raw-hosted"), // composite id -> base "nexus"
            Product("p2", "nexus"), // exact base
            Product("p3", "other"),
            Product("p4", null), // unknown feed
        ];

        List<FeedReportProduct> result = FeedReportService.SelectFeedProducts(
            feeds,
            installed,
            feeds[0]
        );

        result.Select(p => p.ProductId).Should().BeEquivalentTo(["p1", "p2"]);
        result.Single(p => p.ProductId == "p1").Channel.Should().Be("nexus:raw-hosted");
    }
}
