using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class ProductRepositorySnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "storkdrop-repo-tests",
        Guid.NewGuid().ToString("N")
    );

    private string FilePath => Path.Combine(_root, "installed-products.json");

    public ProductRepositorySnapshotTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static InstalledProduct Product(string id, string instance) =>
        new InstalledProduct(id, instance, id, "1.0.0", @"C:\" + id, DateTime.UtcNow);

    private ProductRepository CreateRepository() =>
        new ProductRepository(FilePath, NullLogger<ProductRepository>.Instance);

    [Fact]
    public async Task GetInstances_BeforeInitialization_ReadsTheFileSynchronously()
    {
        await File.WriteAllTextAsync(
            FilePath,
            JsonSerializer.Serialize(
                new List<InstalledProduct>
                {
                    Product("acme", "default"),
                    Product("acme", "second"),
                    Product("other", "default"),
                },
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }
            )
        );
        using ProductRepository repository = CreateRepository();

        IReadOnlyList<InstalledProduct> instances = repository.GetInstances("acme");

        instances.Select(p => p.InstanceId).Should().BeEquivalentTo("default", "second");
    }

    [Fact]
    public void GetInstances_WithoutFile_ReturnsEmpty()
    {
        using ProductRepository repository = CreateRepository();

        repository.GetInstances("acme").Should().BeEmpty();
    }

    [Fact]
    public async Task GetInstances_FollowsAddUpdateAndRemove()
    {
        using ProductRepository repository = CreateRepository();
        await repository.AddAsync(Product("acme", "default"));
        await repository.AddAsync(Product("acme", "second"));

        repository.GetInstances("acme").Should().HaveCount(2);

        await repository.UpdateAsync(Product("acme", "second") with { Version = "2.0.0" });
        repository
            .GetInstances("acme")
            .Single(p => p.InstanceId == "second")
            .Version.Should()
            .Be("2.0.0");

        await repository.RemoveAsync("acme", "default");
        repository.GetInstances("acme").Should().ContainSingle();
    }
}
