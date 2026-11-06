using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Registry;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class FeedRegistryConfigWatcherTests
{
    private static FeedRegistryConfigWatcher Create(TestDirectory dir, IFeedRegistry registry) =>
        new FeedRegistryConfigWatcher(
            dir.Path,
            "config.json",
            registry,
            NullLogger<FeedRegistryConfigWatcher>.Instance,
            TimeSpan.FromMilliseconds(100)
        );

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        return condition();
    }

    [Fact]
    public async Task ReloadsRegistry_WhenConfigFileIsWrittenByAnotherProcess()
    {
        using TestDirectory dir = new TestDirectory();
        IFeedRegistry registry = Substitute.For<IFeedRegistry>();
        using FeedRegistryConfigWatcher watcher = Create(dir, registry);

        await File.WriteAllTextAsync(dir.File("config.json"), "{}");

        (await WaitUntilAsync(() => registry.ReceivedCalls().Any())).Should().BeTrue();
        await registry.Received().ReloadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReloadsRegistry_WhenConfigIsReplacedAtomically()
    {
        using TestDirectory dir = new TestDirectory();
        string config = dir.File("config.json");
        await File.WriteAllTextAsync(config, "{}");
        IFeedRegistry registry = Substitute.For<IFeedRegistry>();
        using FeedRegistryConfigWatcher watcher = Create(dir, registry);

        string temp = dir.File("config.json.tmp-1");
        await File.WriteAllTextAsync(temp, "{\"a\":1}");
        File.Move(temp, config, overwrite: true);

        (await WaitUntilAsync(() => registry.ReceivedCalls().Any())).Should().BeTrue();
    }

    [Fact]
    public async Task CoalescesBurstsOfWritesIntoOneReload()
    {
        using TestDirectory dir = new TestDirectory();
        IFeedRegistry registry = Substitute.For<IFeedRegistry>();
        using FeedRegistryConfigWatcher watcher = Create(dir, registry);

        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(dir.File("config.json"), $"{{\"n\":{i}}}");
            await Task.Delay(10);
        }
        await WaitUntilAsync(() => registry.ReceivedCalls().Any());
        await Task.Delay(400);

        registry.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task IgnoresOtherFilesInTheConfigDirectory()
    {
        using TestDirectory dir = new TestDirectory();
        IFeedRegistry registry = Substitute.For<IFeedRegistry>();
        using FeedRegistryConfigWatcher watcher = Create(dir, registry);

        await File.WriteAllTextAsync(dir.File("other.json"), "{}");
        await Task.Delay(500);

        registry.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task StopsReloading_AfterDispose()
    {
        using TestDirectory dir = new TestDirectory();
        IFeedRegistry registry = Substitute.For<IFeedRegistry>();
        FeedRegistryConfigWatcher watcher = Create(dir, registry);
        watcher.Dispose();

        await File.WriteAllTextAsync(dir.File("config.json"), "{}");
        await Task.Delay(500);

        registry.ReceivedCalls().Should().BeEmpty();
    }
}
