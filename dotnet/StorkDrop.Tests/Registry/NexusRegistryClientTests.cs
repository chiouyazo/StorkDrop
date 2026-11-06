using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry;
using StorkDrop.Registry.Nexus;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class NexusRegistryClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static NexusRegistryClient CreateClient(MockHttpMessageHandler mockHttp)
    {
        NexusOptions options = new NexusOptions
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
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        ProductManifest manifest = new ProductManifest(
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
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
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
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        ProductManifest manifest = new ProductManifest(
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

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task GetProductManifestAsync_Throws_ForErrorsThatAreNotNotFound(
        HttpStatusCode status
    )
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/my-plugin/manifest.json")
            .Respond(status);
        mockHttp
            .When(
                "https://nexus.test.com/repository/storkdrop-releases/my-plugin/versions/1.0.0/manifest.json"
            )
            .Respond(status);
        NexusRegistryClient client = CreateClient(mockHttp);

        Func<Task> latest = () => client.GetProductManifestAsync("my-plugin");
        Func<Task> versioned = () => client.GetProductManifestAsync("my-plugin", "1.0.0");

        (await latest.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should()
            .Be(status);
        (await versioned.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should()
            .Be(status);
    }

    [Fact]
    public async Task GetProductManifestAsync_ForAnotherProductsManifest_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        ProductManifest other = new ProductManifest(
            "other",
            "Other",
            "1.5.0",
            new DateOnly(2025, 3, 1),
            InstallType.Plugin
        );
        mockHttp
            .When(
                "https://nexus.test.com/repository/storkdrop-releases/my-plugin/versions/1.5.0/manifest.json"
            )
            .Respond("application/json", JsonSerializer.Serialize(other, JsonOptions));
        NexusRegistryClient client = CreateClient(mockHttp);

        ProductManifest? result = await client.GetProductManifestAsync("my-plugin", "1.5.0");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProductManifestAsync_WithVersion_RejectsAManifestOfAnotherVersion()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        ProductManifest wrongVersion = new ProductManifest(
            "my-plugin",
            "My Plugin",
            "9.9.9",
            new DateOnly(2025, 3, 1),
            InstallType.Plugin
        );
        mockHttp
            .When(
                "https://nexus.test.com/repository/storkdrop-releases/my-plugin/versions/1.5.0/manifest.json"
            )
            .Respond("application/json", JsonSerializer.Serialize(wrongVersion, JsonOptions));
        NexusRegistryClient client = CreateClient(mockHttp);

        ProductManifest? result = await client.GetProductManifestAsync("my-plugin", "1.5.0");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllProductsAsync_EscapesTheContinuationTokenAndTheRepositoryName()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        const string token = "a+b/c=d&e f";
        NexusComponentSearchResponse first = new NexusComponentSearchResponse
        {
            Items = [],
            ContinuationToken = token,
        };
        NexusComponentSearchResponse second = new NexusComponentSearchResponse { Items = [] };
        mockHttp
            .Expect(
                "https://nexus.test.com/service/rest/v1/components?repository=storkdrop-releases"
            )
            .Respond("application/json", JsonSerializer.Serialize(first));
        mockHttp
            .Expect(
                "https://nexus.test.com/service/rest/v1/components?repository=storkdrop-releases&continuationToken="
                    + Uri.EscapeDataString(token)
            )
            .Respond("application/json", JsonSerializer.Serialize(second));
        NexusRegistryClient client = CreateClient(mockHttp);

        await client.GetAllProductsAsync();

        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_ParsesOnlyRealVersionManifestsAndSortsNumerically()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        NexusComponentSearchResponse response = new NexusComponentSearchResponse
        {
            Items =
            [
                new NexusComponent
                {
                    Assets =
                    [
                        new NexusAsset { Path = "acme/versions/manifest.json" },
                        new NexusAsset
                        {
                            Path = "acme/versions/2.36.1-2386-20260915-100/manifest.json",
                        },
                        new NexusAsset
                        {
                            Path = "acme/versions/2.36.1-2386-20260915-99/manifest.json",
                        },
                        new NexusAsset { Path = "acme/versions/1.0.0/nested/manifest.json" },
                        new NexusAsset { Path = "acme2/versions/9.9.9/manifest.json" },
                        new NexusAsset
                        {
                            Path = "acme/versions/2.36.1-2386-20260915-99/acme-2.36.1.zip",
                        },
                    ],
                },
            ],
        };
        mockHttp
            .When("https://nexus.test.com/service/rest/v1/components?repository=storkdrop-releases")
            .Respond("application/json", JsonSerializer.Serialize(response));
        NexusRegistryClient client = CreateClient(mockHttp);

        IReadOnlyList<string> versions = await client.GetAvailableVersionsAsync("acme");

        versions.Should().Equal("2.36.1-2386-20260915-99", "2.36.1-2386-20260915-100");
    }

    [Fact]
    public async Task GetAllProductsAsync_WhenOneManifestIsForbidden_ListsTheOthers()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        NexusComponentSearchResponse searchResponse = new NexusComponentSearchResponse
        {
            Items =
            [
                new NexusComponent
                {
                    Assets = [new NexusAsset { Path = "plugin-a/manifest.json" }],
                },
                new NexusComponent
                {
                    Assets = [new NexusAsset { Path = "plugin-b/manifest.json" }],
                },
            ],
        };
        mockHttp
            .When("https://nexus.test.com/service/rest/v1/components?repository=storkdrop-releases")
            .Respond("application/json", JsonSerializer.Serialize(searchResponse));
        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/plugin-a/manifest.json")
            .Respond(HttpStatusCode.Forbidden);
        ProductManifest manifestB = new ProductManifest(
            "plugin-b",
            "Plugin B",
            "1.0.0",
            new DateOnly(2025, 1, 1),
            InstallType.Plugin
        );
        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/plugin-b/manifest.json")
            .Respond("application/json", JsonSerializer.Serialize(manifestB, JsonOptions));
        NexusRegistryClient client = CreateClient(mockHttp);

        IReadOnlyList<ProductManifest> result = await client.GetAllProductsAsync();

        result.Should().ContainSingle().Which.ProductId.Should().Be("plugin-b");
    }

    [Fact]
    public async Task GetAllProductsAsync_WhenCredentialsAreRejected_Throws()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        NexusComponentSearchResponse searchResponse = new NexusComponentSearchResponse
        {
            Items =
            [
                new NexusComponent
                {
                    Assets = [new NexusAsset { Path = "plugin-a/manifest.json" }],
                },
            ],
        };
        mockHttp
            .When("https://nexus.test.com/service/rest/v1/components?repository=storkdrop-releases")
            .Respond("application/json", JsonSerializer.Serialize(searchResponse));
        mockHttp
            .When("https://nexus.test.com/repository/storkdrop-releases/plugin-a/manifest.json")
            .Respond(HttpStatusCode.Unauthorized);
        NexusRegistryClient client = CreateClient(mockHttp);

        Func<Task> list = () => client.GetAllProductsAsync();

        await list.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue_WhenServerResponds()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
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
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
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
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
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
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        byte[] content = "fake-zip-content"u8.ToArray();
        mockHttp
            .When(
                "https://nexus.test.com/repository/storkdrop-releases/my-plugin/versions/1.0.0/my-plugin-1.0.0.zip"
            )
            .Respond("application/zip", new MemoryStream(content));

        NexusRegistryClient client = CreateClient(mockHttp);

        Stream result = await client.DownloadProductAsync("my-plugin", "1.0.0");

        using MemoryStream ms = new MemoryStream();
        await result.CopyToAsync(ms);
        ms.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAllProductsAsync_ParsesAssetList()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        NexusComponentSearchResponse searchResponse = new NexusComponentSearchResponse
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

        ProductManifest manifestA = new ProductManifest(
            "plugin-a",
            "Plugin A",
            "2.0.0",
            new DateOnly(2025, 1, 1),
            InstallType.Plugin
        );
        ProductManifest manifestB = new ProductManifest(
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
