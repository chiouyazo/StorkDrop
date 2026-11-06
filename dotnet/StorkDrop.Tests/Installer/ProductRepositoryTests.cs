using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class ProductRepositoryTests
{
    private static ProductRepository Create(string path) =>
        new ProductRepository(path, NullLogger<ProductRepository>.Instance);

    private static InstalledProduct Product(string id, string instance = "default") =>
        new InstalledProduct(
            ProductId: id,
            InstanceId: instance,
            Title: id,
            Version: "1.0.0",
            InstalledPath: $@"C:\{id}",
            InstalledDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );

    [Fact]
    public async Task AddAsync_FromTwoRepositoryInstances_KeepsBothProducts()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        using ProductRepository gui = Create(path);
        using ProductRepository cli = Create(path);
        await gui.InitializeAsync();
        await cli.InitializeAsync();

        await cli.AddAsync(Product("from-cli"));
        await gui.AddAsync(Product("from-gui"));

        using ProductRepository reader = Create(path);
        IReadOnlyList<InstalledProduct> all = await reader.GetAllAsync();
        all.Select(p => p.ProductId).Should().BeEquivalentTo(["from-cli", "from-gui"]);
    }

    [Fact]
    public async Task RemoveAsync_WithStaleCache_DoesNotResurrectProductsAddedByAnotherProcess()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        using ProductRepository gui = Create(path);
        using ProductRepository cli = Create(path);
        await gui.AddAsync(Product("shared"));
        await gui.GetAllAsync();

        await cli.AddAsync(Product("added-by-cli"));
        await gui.RemoveAsync("shared");

        using ProductRepository reader = Create(path);
        (await reader.GetAllAsync()).Select(p => p.ProductId).Should().Equal("added-by-cli");
    }

    [Fact]
    public async Task GetAllAsync_SeesChangesWrittenByAnotherInstanceAfterFirstLoad()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        using ProductRepository gui = Create(path);
        using ProductRepository cli = Create(path);
        (await gui.GetAllAsync()).Should().BeEmpty();

        await cli.AddAsync(Product("late"));

        (await gui.GetAllAsync()).Select(p => p.ProductId).Should().Equal("late");
        (await gui.GetByIdAsync("late")).Should().NotBeNull();
    }

    [Fact]
    public async Task ParallelAddsFromManyInstances_LoseNothing()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");

        await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(async i =>
                {
                    using ProductRepository repository = Create(path);
                    await repository.AddAsync(Product($"p{i}"));
                })
        );

        using ProductRepository reader = Create(path);
        (await reader.GetAllAsync()).Should().HaveCount(12);
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToBackup_WhenFileIsCorrupt()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        using (ProductRepository first = Create(path))
        {
            await first.AddAsync(Product("a"));
            await first.AddAsync(Product("b"));
        }
        await File.WriteAllTextAsync(path, "{ truncated");

        using ProductRepository repository = Create(path);
        IReadOnlyList<InstalledProduct> all = await repository.GetAllAsync();

        all.Select(p => p.ProductId).Should().Equal("a");
    }

    [Fact]
    public async Task AddAsync_AfterCorruptFile_DoesNotOverwriteLastGoodBackup()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        using (ProductRepository first = Create(path))
        {
            await first.AddAsync(Product("a"));
            await first.AddAsync(Product("b"));
        }
        await File.WriteAllTextAsync(path, "{ truncated");
        string backupBefore = await File.ReadAllTextAsync(AtomicFile.BackupPath(path));

        using ProductRepository repository = Create(path);
        await repository.AddAsync(Product("c"));

        (await File.ReadAllTextAsync(AtomicFile.BackupPath(path))).Should().Be(backupBefore);
        (await repository.GetAllAsync()).Select(p => p.ProductId).Should().Equal("a", "c");
    }

    [Fact]
    public async Task InitializeAsync_PersistsInstanceIdMigration()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        await File.WriteAllTextAsync(
            path,
            """[{"ProductId":"legacy","InstanceId":"","Title":"L","Version":"1","InstalledPath":"C:\\l","InstalledDate":"2026-01-01T00:00:00Z"}]"""
        );

        using ProductRepository repository = Create(path);
        await repository.InitializeAsync();

        (await File.ReadAllTextAsync(path)).Should().Contain("\"InstanceId\": \"default\"");
    }

    [Fact]
    public async Task AddAsync_LeavesNoLockOrTempFilesBehind()
    {
        using TestDirectory dir = new TestDirectory();
        string path = dir.File("installed-products.json");
        using ProductRepository repository = Create(path);

        await repository.AddAsync(Product("a"));
        await repository.UpdateAsync(Product("a") with { Version = "2.0.0" });

        Directory
            .GetFiles(dir.Path)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo(["installed-products.json", "installed-products.json.bak"]);
    }
}
