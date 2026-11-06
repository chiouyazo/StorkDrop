using System.IO;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StorkDrop.App.Services.Cli;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Cli;

public sealed class CliQueryServiceTests
{
    private readonly IFeedRegistry _feeds = Substitute.For<IFeedRegistry>();
    private readonly IProductRepository _repository = Substitute.For<IProductRepository>();
    private readonly List<FeedInfo> _feedInfos = [];
    private readonly StringWriter _output = new StringWriter();
    private readonly StringWriter _error = new StringWriter();

    private CliQueryService Service() => new(_feeds, _repository, _output, _error);

    private IRegistryClient AddFeed(string id, string name)
    {
        IRegistryClient client = Substitute.For<IRegistryClient>();
        _feedInfos.Add(new FeedInfo(id, name));
        _feeds.GetFeeds().Returns(_feedInfos.ToList());
        _feeds.GetClient(id).Returns(client);
        return client;
    }

    private static ProductManifest Manifest(
        string id,
        string version,
        string? badge = null,
        InstallType type = InstallType.Executable
    ) => new(id, id.ToUpperInvariant(), version, new DateOnly(2026, 1, 1), type, BadgeText: badge);

    private static InstalledProduct Installed(
        string id,
        string version,
        string? feedId,
        string instance = "default",
        string? badge = null
    ) =>
        new(
            id,
            instance,
            id.ToUpperInvariant(),
            version,
            $@"C:\Apps\{id}",
            new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            FeedId: feedId,
            BadgeText: badge,
            InstanceUniqueId: instance == "default" ? null : "u-" + instance
        );

    private JsonElement Output() => JsonDocument.Parse(_output.ToString()).RootElement;

    [Fact]
    public async Task Installed_Json_ListsInstanceVersionFeedAndBadge()
    {
        AddFeed("f1", "Prod");
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([
                Installed("b-app", "2.0.0", "f1", badge: "EPIC"),
                Installed("a-app", "1.0.0", "f1"),
                Installed("a-app", "1.5.0", "f1", instance: "test"),
            ]);

        int code = await Service().InstalledAsync(null, json: true);

        code.Should().Be(CliExitCodes.Success);
        JsonElement products = Output().GetProperty("products");
        products.GetArrayLength().Should().Be(3);
        products[0].GetProperty("productId").GetString().Should().Be("a-app");
        products[0].GetProperty("instanceId").GetString().Should().Be("default");
        products[1].GetProperty("instanceId").GetString().Should().Be("test");
        products[1].GetProperty("instanceUniqueId").GetString().Should().Be("u-test");
        products[1].GetProperty("version").GetString().Should().Be("1.5.0");
        products[2].GetProperty("feedId").GetString().Should().Be("f1");
        products[2].GetProperty("feedName").GetString().Should().Be("Prod");
        products[2].GetProperty("badgeText").GetString().Should().Be("EPIC");
    }

    [Fact]
    public async Task Installed_FeedFilter_OnlyReturnsProductsOfThatFeed()
    {
        AddFeed("f1", "Prod");
        AddFeed("f2", "Dev");
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("a", "1.0.0", "f1"), Installed("b", "1.0.0", "f2")]);

        await Service().InstalledAsync("Dev", json: true);

        JsonElement products = Output().GetProperty("products");
        products.GetArrayLength().Should().Be(1);
        products[0].GetProperty("productId").GetString().Should().Be("b");
    }

    [Fact]
    public async Task Installed_UnknownFeed_ThrowsNotFound()
    {
        AddFeed("f1", "Prod");

        Func<Task> act = () => Service().InstalledAsync("nope", json: true);

        (await act.Should().ThrowAsync<CliException>())
            .Which.ExitCode.Should()
            .Be(CliExitCodes.NotFound);
    }

    [Fact]
    public async Task Installed_Text_PrintsHeaderAndRows()
    {
        AddFeed("f1", "Prod");
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("a", "1.0.0", "f1")]);

        await Service().InstalledAsync(null, json: false);

        _output.ToString().Should().Contain("Product ID").And.Contain("1.0.0").And.Contain("Prod");
    }

    [Fact]
    public async Task Outdated_ReportsOnlyNewerVersionsFromOwnFeed()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        IRegistryClient dev = AddFeed("f2", "Dev");
        prod.GetProductManifestAsync("old", Arg.Any<CancellationToken>())
            .Returns(Manifest("old", "2.0.0"));
        prod.GetProductManifestAsync("current", Arg.Any<CancellationToken>())
            .Returns(Manifest("current", "1.0.0"));
        dev.GetProductManifestAsync("old", Arg.Any<CancellationToken>())
            .Returns(Manifest("old", "9.0.0"));
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("old", "1.0.0", "f1"), Installed("current", "1.0.0", "f1")]);

        int code = await Service().OutdatedAsync(null, json: true);

        code.Should().Be(CliExitCodes.Success);
        JsonElement products = Output().GetProperty("products");
        products.GetArrayLength().Should().Be(1);
        products[0].GetProperty("productId").GetString().Should().Be("old");
        products[0].GetProperty("installedVersion").GetString().Should().Be("1.0.0");
        products[0].GetProperty("availableVersion").GetString().Should().Be("2.0.0");
        products[0].GetProperty("feedId").GetString().Should().Be("f1");
        Output().GetProperty("errors").GetArrayLength().Should().Be(0);
        await dev.DidNotReceive()
            .GetProductManifestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outdated_IsReadOnly_NeverDownloads()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetProductManifestAsync("old", Arg.Any<CancellationToken>())
            .Returns(Manifest("old", "2.0.0"));
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("old", "1.0.0", "f1")]);

        await Service().OutdatedAsync(null, json: true);

        await prod.DidNotReceiveWithAnyArgs().DownloadProductAsync(default!, default!, default);
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Outdated_QueriesEachFeedProductPairOnce()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetProductManifestAsync("app", Arg.Any<CancellationToken>())
            .Returns(Manifest("app", "2.0.0"));
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([
                Installed("app", "1.0.0", "f1"),
                Installed("app", "1.1.0", "f1", instance: "test"),
            ]);

        await Service().OutdatedAsync(null, json: true);

        Output().GetProperty("products").GetArrayLength().Should().Be(2);
        await prod.Received(1).GetProductManifestAsync("app", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outdated_FeedThrows_ReportsErrorAndFeedErrorExitCode()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetProductManifestAsync("app", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("app", "1.0.0", "f1")]);

        int code = await Service().OutdatedAsync(null, json: true);

        code.Should().Be(CliExitCodes.FeedError);
        JsonElement errors = Output().GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("message").GetString().Should().Contain("401");
        errors[0].GetProperty("productId").GetString().Should().Be("app");
    }

    [Fact]
    public async Task Outdated_ProductWithoutFeedId_IsReportedAsError()
    {
        AddFeed("f1", "Prod");
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("app", "1.0.0", null)]);

        int code = await Service().OutdatedAsync(null, json: true);

        code.Should().Be(CliExitCodes.FeedError);
        Output().GetProperty("errors").GetArrayLength().Should().Be(1);
        Output().GetProperty("products").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Outdated_FeedNoLongerConfigured_IsReportedAsError()
    {
        AddFeed("f1", "Prod");
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("app", "1.0.0", "gone")]);

        int code = await Service().OutdatedAsync(null, json: true);

        code.Should().Be(CliExitCodes.FeedError);
        Output()
            .GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Contain("not configured");
    }

    [Fact]
    public async Task Outdated_ProductMissingInFeed_IsReportedAsError()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetProductManifestAsync("app", Arg.Any<CancellationToken>())
            .Returns((ProductManifest?)null);
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("app", "1.0.0", "f1")]);

        int code = await Service().OutdatedAsync(null, json: true);

        code.Should().Be(CliExitCodes.FeedError);
    }

    [Fact]
    public async Task Json_OutputContainsNoLogOrTextOutsideTheDocument()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetAllProductsAsync(Arg.Any<CancellationToken>())
            .Returns([Manifest("app", "1.0.0", badge: "EPIC")]);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await Service().ListJsonAsync(null);

        Action parse = () => JsonDocument.Parse(_output.ToString());
        parse.Should().NotThrow();
    }

    [Fact]
    public async Task ListJson_ContainsProductsInstalledInstancesAndBadge()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetAllProductsAsync(Arg.Any<CancellationToken>())
            .Returns([Manifest("app", "2.0.0", badge: "EPIC")]);
        _repository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Installed("app", "1.0.0", "f1")]);

        int code = await Service().ListJsonAsync(null);

        code.Should().Be(CliExitCodes.Success);
        JsonElement product = Output().GetProperty("products")[0];
        product.GetProperty("version").GetString().Should().Be("2.0.0");
        product.GetProperty("installType").GetString().Should().Be("executable");
        product.GetProperty("badgeText").GetString().Should().Be("EPIC");
        product.GetProperty("feedId").GetString().Should().Be("f1");
        product.GetProperty("installed")[0].GetProperty("version").GetString().Should().Be("1.0.0");
    }

    [Fact]
    public async Task ListJson_FeedFailure_StillPrintsOtherFeedsAndReturnsFeedError()
    {
        IRegistryClient bad = AddFeed("f1", "Bad");
        IRegistryClient good = AddFeed("f2", "Good");
        bad.GetAllProductsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));
        good.GetAllProductsAsync(Arg.Any<CancellationToken>()).Returns([Manifest("app", "1.0.0")]);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        int code = await Service().ListJsonAsync(null);

        code.Should().Be(CliExitCodes.FeedError);
        Output().GetProperty("products").GetArrayLength().Should().Be(1);
        Output().GetProperty("errors")[0].GetProperty("feedId").GetString().Should().Be("f1");
        _error.ToString().Should().Contain("Bad");
    }

    [Fact]
    public async Task Versions_Json_ListsVersionsPerFeed()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetAvailableVersionsAsync("app", Arg.Any<CancellationToken>())
            .Returns(["1.0.0", "2.0.0"]);

        int code = await Service().VersionsAsync("app", null, json: true);

        code.Should().Be(CliExitCodes.Success);
        JsonElement feed = Output().GetProperty("feeds")[0];
        feed.GetProperty("feedName").GetString().Should().Be("Prod");
        feed.GetProperty("versions").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Versions_Text_KeepsLegacyFormat()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetAvailableVersionsAsync("app", Arg.Any<CancellationToken>()).Returns(["1.0.0"]);

        await Service().VersionsAsync("app", null, json: false);

        _output
            .ToString()
            .Should()
            .Be(
                $"Versions for 'app' on feed 'Prod':{Environment.NewLine}  1.0.0{Environment.NewLine}"
            );
    }

    [Fact]
    public async Task Versions_UnknownProduct_ReturnsNotFound()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetAvailableVersionsAsync("app", Arg.Any<CancellationToken>()).Returns([]);

        int code = await Service().VersionsAsync("app", null, json: false);

        code.Should().Be(CliExitCodes.NotFound);
    }

    [Fact]
    public async Task Versions_FeedThrows_ReturnsFeedErrorInsteadOfNotFound()
    {
        IRegistryClient prod = AddFeed("f1", "Prod");
        prod.GetAvailableVersionsAsync("app", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("401"));

        int code = await Service().VersionsAsync("app", null, json: true);

        code.Should().Be(CliExitCodes.FeedError);
        Output().GetProperty("errors").GetArrayLength().Should().Be(1);
    }
}
