using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class InstallerRegistrationTests
{
    [Fact]
    public void AddInstaller_ResolvesFeedReportServiceAndItsDependents()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddInstaller();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFeedReportService>().Should().NotBeNull();
        provider.GetRequiredService<UninstallService>().Should().NotBeNull();
    }
}
