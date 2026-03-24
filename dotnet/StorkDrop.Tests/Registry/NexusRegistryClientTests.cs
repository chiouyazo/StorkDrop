using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class NexusRegistryClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static NexusRegistryClient CreateClient(MockHttpMessageHandler mockHttp)
    {
        NexusOptions options = new()
        {
            BaseUrl = "https://nexus.test.com",
            Repository = "storkdrop-releases",
        };

        HttpClient httpClient = mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri(options.BaseUrl);

        return new NexusRegistryClient(
            httpClient,
            Options.Create(options),
            NullLogger<NexusRegistryClient>.Instance
        );
    }

    [Fact]
    public async Task GetProductManifestAsync_ReturnsManifest_WhenFound()
    {
        MockHttpMessageHandler mockHttp = new();
        ProductManifest manifest = new(
            ProductId: "my-plugin",
            Title: "My Plugin",
            Version: "2.1.0",
            ReleaseDate: new DateOnly(2025, 6, 1),
            InstallType: InstallType.Plugin,
            Description: "A great plugin"
        );

        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/my-plugin/manifest.json")
            .Respond("application/json", JsonSerializer.Serialize(manifest, JsonOptions));

        NexusRegistryClient client = CreateClient(mockHttp);

        ProductManifest? result = await client.GetProductManifestAsync("my-plugin");

        result.Should().NotBeNull();
        result!.ProductId.Should().Be("my-plugin");
        result.Title.Should().Be("My Plugin");
        result.Version.Should().Be("2.1.0");
        result.InstallType.Should().Be(InstallType.Plugin);
    }

    [Fact]
    public async Task GetProductManifestAsync_ReturnsNull_WhenNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/missing/manifest.json")
            .Respond(HttpStatusCode.NotFound);

        NexusRegistryClient client = CreateClient(mockHttp);

        ProductManifest? result = await client.GetProductManifestAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProductManifestAsync_WithVersion_ReturnsVersionedManifest()
    {
        MockHttpMessageHandler mockHttp = new();
        ProductManifest manifest = new(
            ProductId: "my-plugin",
            Title: "My Plugin",
            Version: "1.5.0",
            ReleaseDate: new DateOnly(2025, 3, 1),
            InstallType: InstallType.Plugin
        );

        mockHttp
            .When(
                "https://nexus.test.com/repository/storkdrop-releases/my-plugin/versions/1.5.0/manifest.json"
            )
            .Respond("application/json", JsonSerializer.Serialize(manifest, JsonOptions));

        NexusRegistryClient client = CreateClient(mockHttp);

        ProductManifest? result = await client.GetProductManifestAsync("my-plugin", "1.5.0");

        result.Should().NotBeNull();
        result!.Version.Should().Be("1.5.0");
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue_WhenServerResponds()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://nexus.test.com/service/rest/v1/repositories")
            .Respond(HttpStatusCode.OK);

        NexusRegistryClient client = CreateClient(mockHttp);

        bool result = await client.TestConnectionAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_WhenServerReturnsError()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://nexus.test.com/service/rest/v1/repositories")
            .Respond(HttpStatusCode.Unauthorized);

        NexusRegistryClient client = CreateClient(mockHttp);

        bool result = await client.TestConnectionAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_WhenConnectionFails()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://nexus.test.com/service/rest/v1/repositories")
            .Throw(new HttpRequestException("Connection refused"));

        NexusRegistryClient client = CreateClient(mockHttp);

        bool result = await client.TestConnectionAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadProductAsync_ReturnsStream()
    {
        MockHttpMessageHandler mockHttp = new();
        byte[] content = "fake-zip-content"u8.ToArray();
        mockHttp
            .When(
                "https://nexus.test.com/repository/storkdrop-releases/my-plugin/versions/1.0.0/my-plugin-1.0.0.zip"
            )
            .Respond("application/zip", new MemoryStream(content));

        NexusRegistryClient client = CreateClient(mockHttp);

        Stream result = await client.DownloadProductAsync("my-plugin", "1.0.0");

        using MemoryStream ms = new();
        await result.CopyToAsync(ms);
        ms.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAllProductsAsync_ParsesAssetList()
    {
        MockHttpMessageHandler mockHttp = new();

        NexusComponentSearchResponse searchResponse = new()
        {
            Items =
            [
                new NexusComponent
                {
                    Name = "/plugin-a/manifest.json",
                    Assets = [new NexusAsset { Path = "plugin-a/manifest.json" }],
                },
                new NexusComponent
                {
                    Name = "/plugin-b/manifest.json",
                    Assets = [new NexusAsset { Path = "plugin-b/manifest.json" }],
                },
                new NexusComponent
                {
                    Name = "/plugin-a/versions/1.0.0/manifest.json",
                    Assets = [new NexusAsset { Path = "plugin-a/versions/1.0.0/manifest.json" }],
                },
            ],
            ContinuationToken = null,
        };

        mockHttp
            .When("https://nexus.test.com/service/rest/v1/components?repository=storkdrop-releases")
            .Respond("application/json", JsonSerializer.Serialize(searchResponse));

        ProductManifest manifestA = new(
            "plugin-a",
            "Plugin A",
            "2.0.0",
            new DateOnly(2025, 1, 1),
            InstallType.Plugin
        );
        ProductManifest manifestB = new(
            "plugin-b",
            "Plugin B",
            "1.0.0",
            new DateOnly(2025, 1, 1),
            InstallType.Plugin
        );

        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/plugin-a/manifest.json")
            .Respond("application/json", JsonSerializer.Serialize(manifestA, JsonOptions));
        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/plugin-b/manifest.json")
            .Respond("application/json", JsonSerializer.Serialize(manifestB, JsonOptions));

        NexusRegistryClient client = CreateClient(mockHttp);

        IReadOnlyList<ProductManifest> result = await client.GetAllProductsAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(m => m.ProductId == "plugin-a");
        result.Should().Contain(m => m.ProductId == "plugin-b");
    }
}
