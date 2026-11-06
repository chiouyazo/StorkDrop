using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry.S3;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class S3RegistryClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static ProductManifest Manifest(string productId, string version) =>
        new ProductManifest(
            productId,
            productId,
            version,
            new DateOnly(2026, 1, 1),
            InstallType.Executable
        );

    private static GetObjectResponse Json(ProductManifest manifest) =>
        new GetObjectResponse
        {
            ResponseStream = new MemoryStream(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions))
            ),
        };

    private static AmazonS3Exception Status(HttpStatusCode status) =>
        new AmazonS3Exception(status.ToString()) { StatusCode = status };

    private static IAmazonS3 Bucket(
        string[] productIds,
        Func<string, GetObjectResponse> latestManifest
    )
    {
        IAmazonS3 s3 = Substitute.For<IAmazonS3>();
        s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListObjectsV2Response
                {
                    CommonPrefixes = productIds.Select(id => $"prod/{id}/").ToList(),
                    IsTruncated = false,
                }
            );
        foreach (string id in productIds)
        {
            string key = $"prod/{id}/manifest.json";
            s3.GetObjectAsync(
                    Arg.Is<GetObjectRequest>(r => r.Key == key),
                    Arg.Any<CancellationToken>()
                )
                .Returns(_ => Task.FromResult(latestManifest(id)));
        }

        return s3;
    }

    private static S3RegistryClient CreateClient(IAmazonS3 s3) =>
        new S3RegistryClient(
            s3,
            "bucket",
            new S3Layout(null),
            "prod",
            NullLogger<S3RegistryClient>.Instance
        );

    [Fact]
    public async Task GetAllProducts_WhenOneProductIsForbidden_ListsTheOthers()
    {
        IAmazonS3 s3 = Bucket(
            ["a", "b", "c"],
            id => id == "b" ? throw Status(HttpStatusCode.Forbidden) : Json(Manifest(id, "1.0.0"))
        );

        IReadOnlyList<ProductManifest> products = await CreateClient(s3).GetAllProductsAsync();

        products.Select(p => p.ProductId).Should().BeEquivalentTo("a", "c");
    }

    [Fact]
    public async Task GetAllProducts_WhenEveryProductFails_SurfacesTheError()
    {
        IAmazonS3 s3 = Bucket(["a", "b"], _ => throw Status(HttpStatusCode.Forbidden));

        Func<Task> list = () => CreateClient(s3).GetAllProductsAsync();

        await list.Should().ThrowAsync<AmazonS3Exception>();
    }

    [Fact]
    public async Task GetAllProducts_WhenAProductHasNoManifest_StillListsTheOthers()
    {
        IAmazonS3 s3 = Bucket(
            ["a", "b"],
            id => id == "a" ? throw Status(HttpStatusCode.NotFound) : Json(Manifest(id, "1.0.0"))
        );

        IReadOnlyList<ProductManifest> products = await CreateClient(s3).GetAllProductsAsync();

        products.Should().ContainSingle().Which.ProductId.Should().Be("b");
    }

    [Fact]
    public async Task GetProductManifest_ForAnotherProductsManifest_IsRejected()
    {
        IAmazonS3 s3 = Bucket(["a"], _ => Json(Manifest("someone-else", "1.0.0")));

        ProductManifest? manifest = await CreateClient(s3).GetProductManifestAsync("a");

        manifest.Should().BeNull();
    }

    [Fact]
    public async Task GetProductManifest_WithVersion_RequiresTheRequestedVersion()
    {
        IAmazonS3 s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectAsync(
                Arg.Is<GetObjectRequest>(r => r.Key == "prod/a/versions/1.0.0/manifest.json"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Json(Manifest("a", "2.0.0"))));
        s3.GetObjectAsync(
                Arg.Is<GetObjectRequest>(r => r.Key == "prod/a/versions/2.0.0/manifest.json"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Json(Manifest("a", "2.0.0"))));
        S3RegistryClient client = CreateClient(s3);

        ProductManifest? mislabelled = await client.GetProductManifestAsync("a", "1.0.0");
        ProductManifest? correct = await client.GetProductManifestAsync("a", "2.0.0");

        mislabelled.Should().BeNull();
        correct.Should().NotBeNull();
    }
}
