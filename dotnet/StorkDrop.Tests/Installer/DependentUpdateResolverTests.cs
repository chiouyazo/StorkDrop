using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class DependentUpdateResolverTests
{
    private readonly IProductRepository _repo = Substitute.For<IProductRepository>();
    private readonly IFeedRegistry _feeds = Substitute.For<IFeedRegistry>();
    private readonly Dictionary<string, IRegistryClient> _clients = new();
    private readonly List<InstalledProduct> _installed = [];

    private DependentUpdateResolver Resolver() =>
        new(_repo, _feeds, NullLogger<DependentUpdateResolver>.Instance);

    // Registers an installed product plus the "latest" manifest its channel currently serves.
    private void Add(
        string productId,
        string installedVersion,
        string latestVersion,
        string[]? requires = null,
        string feedId = "feed",
        string instanceId = "default"
    )
    {
        _installed.Add(
            new InstalledProduct(
                productId,
                instanceId,
                productId,
                installedVersion,
                $@"C:\Apps\{productId}",
                default,
                FeedId: feedId
            )
        );
        _repo
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<InstalledProduct>>(_installed));

        IRegistryClient client = ClientFor(feedId);
        ProductManifest manifest = new ProductManifest(
            productId,
            productId,
            latestVersion,
            default,
            InstallType.Suite,
            RequiredProductIds: requires
        );
        client
            .GetProductManifestAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProductManifest?>(manifest));
    }

    private IRegistryClient ClientFor(string feedId)
    {
        if (!_clients.TryGetValue(feedId, out IRegistryClient? client))
        {
            client = Substitute.For<IRegistryClient>();
            _clients[feedId] = client;
            _feeds.GetClient(feedId).Returns(client);
        }
        return client;
    }

    [Fact]
    public async Task Direct_dependent_with_an_update_is_returned()
    {
        Add("parent", "1.0.0", "1.1.0");
        Add("child", "1.0.0", "1.1.0", requires: ["parent"]);

        IReadOnlyList<DependentUpdate> result = await Resolver().ResolveAsync("parent");

        result.Select(d => d.Installed.ProductId).Should().Equal("child");
        result[0].TargetVersion.Should().Be("1.1.0");
    }

    [Fact]
    public async Task Transitive_chain_is_followed_parents_first()
    {
        Add("x", "1.0.0", "2.0.0");
        Add("y", "1.0.0", "2.0.0", requires: ["x"]);
        Add("z", "1.0.0", "2.0.0", requires: ["y"]);

        IReadOnlyList<DependentUpdate> result = await Resolver().ResolveAsync("x");

        result.Select(d => d.Installed.ProductId).Should().Equal("y", "z");
    }

    [Fact]
    public async Task Cyclic_dependencies_do_not_loop()
    {
        Add("a", "1.0.0", "2.0.0", requires: ["b"]);
        Add("b", "1.0.0", "2.0.0", requires: ["a"]);

        IReadOnlyList<DependentUpdate> result = await Resolver().ResolveAsync("a");

        // b is offered, a (the updated product) is never re-offered, and it terminates.
        result.Select(d => d.Installed.ProductId).Should().Equal("b");
    }

    [Fact]
    public async Task Dependent_without_an_available_update_is_excluded()
    {
        Add("parent", "1.0.0", "1.1.0");
        Add("child", "1.0.0", "1.0.0", requires: ["parent"]);

        IReadOnlyList<DependentUpdate> result = await Resolver().ResolveAsync("parent");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Availability_is_checked_on_the_dependents_own_channel()
    {
        Add("parent", "1.0.0", "1.1.0", feedId: "prod");
        Add("child", "1.0.0", "1.1.0", requires: ["parent"], feedId: "child-channel");

        IReadOnlyList<DependentUpdate> result = await Resolver().ResolveAsync("parent");

        result.Select(d => d.Installed.ProductId).Should().Equal("child");
        _feeds.Received().GetClient("child-channel");
    }

    [Fact]
    public async Task The_updated_product_itself_is_never_returned()
    {
        Add("solo", "1.0.0", "2.0.0");

        IReadOnlyList<DependentUpdate> result = await Resolver().ResolveAsync("solo");

        result.Should().BeEmpty();
    }
}
