using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using StorkDrop.App.Services;
using StorkDrop.App.Views;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Cli;

public sealed class NoAutomaticInstallPolicyTests
{
    private static readonly Type[] InstallCapableTypes =
    [
        typeof(IInstallationEngine),
        typeof(InstallationCoordinator),
        typeof(InstallationTracker),
        typeof(DependentUpdateService),
        typeof(SelfUpdateService),
        typeof(IServiceProvider),
    ];

    private static IEnumerable<Type> BackgroundServiceTypes() =>
        typeof(UpdateBackgroundService)
            .Assembly.GetTypes()
            .Where(t => typeof(IHostedService).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<Type> DependencyTypes(Type type)
    {
        const BindingFlags all =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        foreach (ConstructorInfo ctor in type.GetConstructors(all))
        {
            foreach (ParameterInfo parameter in ctor.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (FieldInfo field in type.GetFields(all))
            yield return field.FieldType;
    }

    [Fact]
    public void BackgroundServices_AreDiscovered()
    {
        BackgroundServiceTypes()
            .Should()
            .Contain(typeof(UpdateBackgroundService))
            .And.Contain(typeof(FeedReportFlushBackgroundService));
    }

    [Fact]
    public void BackgroundServices_DoNotDependOnAnythingThatCanInstall()
    {
        foreach (Type service in BackgroundServiceTypes())
        {
            foreach (Type dependency in DependencyTypes(service))
            {
                InstallCapableTypes
                    .Should()
                    .NotContain(
                        t => t.IsAssignableFrom(dependency),
                        $"{service.Name} must only report, but depends on {dependency.Name}"
                    );
                dependency
                    .Namespace.Should()
                    .NotBe(
                        "StorkDrop.App.ViewModels",
                        $"{service.Name} must not drive view models that can install"
                    );
            }
        }
    }

    [Fact]
    public void UpdateBackgroundService_ReportsThroughNotificationServiceOnly()
    {
        IEnumerable<Type> dependencies = DependencyTypes(typeof(UpdateBackgroundService));

        dependencies.Should().Contain(typeof(INotificationService));
        typeof(INotificationService)
            .GetMethods()
            .Select(m => m.Name)
            .Should()
            .OnlyContain(name => name.StartsWith("Show"));
    }

    [Fact]
    public void AlreadyInstalledPostProduct_CanNeverBeSelected()
    {
        ProductManifest manifest = new(
            "dep",
            "Dep",
            "1.0.0",
            new DateOnly(2026, 1, 1),
            InstallType.Plugin
        );
        PostProductItem item = new(new ResolvedPostProduct(manifest, "feed", "Feed"), true);

        item.IsSelected = true;

        item.IsSelected.Should().BeFalse();
    }
}
