using FluentAssertions;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class UpdateNotificationDebouncerTests
{
    private static InstalledProduct Installed(string instanceId = "default") =>
        new InstalledProduct(
            "acme",
            instanceId,
            "Acme",
            "1.0.0",
            @"C:\acme",
            DateTime.UtcNow,
            "feed"
        );

    private static AvailableUpdate Update(string version, string instanceId = "default") =>
        new AvailableUpdate(
            Installed(instanceId),
            new ProductManifest(
                "acme",
                "Acme",
                version,
                new DateOnly(2026, 1, 1),
                InstallType.Executable
            )
        );

    [Fact]
    public void ShouldNotify_AnnouncesTheSameVersionOnlyOnce()
    {
        UpdateNotificationDebouncer debouncer = new UpdateNotificationDebouncer();

        debouncer.ShouldNotify(Update("1.1.0")).Should().BeTrue();
        debouncer.ShouldNotify(Update("1.1.0")).Should().BeFalse();
        debouncer.ShouldNotify(Update("1.1.0")).Should().BeFalse();
    }

    [Fact]
    public void ShouldNotify_AnnouncesAgainWhenAnEvenNewerVersionAppears()
    {
        UpdateNotificationDebouncer debouncer = new UpdateNotificationDebouncer();
        debouncer.ShouldNotify(Update("1.1.0"));

        debouncer.ShouldNotify(Update("1.2.0")).Should().BeTrue();
    }

    [Fact]
    public void ShouldNotify_TracksInstancesSeparately()
    {
        UpdateNotificationDebouncer debouncer = new UpdateNotificationDebouncer();
        debouncer.ShouldNotify(Update("1.1.0", "default"));

        debouncer.ShouldNotify(Update("1.1.0", "second")).Should().BeTrue();
    }

    [Fact]
    public void Forget_ResetsInstancesThatWereCheckedAndHaveNoUpdateAnymore()
    {
        UpdateNotificationDebouncer debouncer = new UpdateNotificationDebouncer();
        debouncer.ShouldNotify(Update("1.1.0"));

        debouncer.Forget([Installed()], []);

        debouncer.ShouldNotify(Update("1.1.0")).Should().BeTrue();
    }

    [Fact]
    public void Forget_KeepsInstancesThatCouldNotBeChecked()
    {
        UpdateNotificationDebouncer debouncer = new UpdateNotificationDebouncer();
        debouncer.ShouldNotify(Update("1.1.0"));

        debouncer.Forget([], []);

        debouncer.ShouldNotify(Update("1.1.0")).Should().BeFalse();
    }
}
