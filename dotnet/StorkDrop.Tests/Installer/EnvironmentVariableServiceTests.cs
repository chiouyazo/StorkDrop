using FluentAssertions;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class EnvironmentVariableServiceTests
{
    [Fact]
    public void ResolveTemplates_ShouldReplaceInstallPath()
    {
        string result = EnvironmentVariableService.ResolveTemplates(
            @"{InstallPath}\bin",
            @"C:\Program Files\Acme"
        );

        result.Should().Be(@"C:\Program Files\Acme\bin");
    }

    [Fact]
    public void ResolveTemplates_ShouldBeCaseInsensitive()
    {
        string result = EnvironmentVariableService.ResolveTemplates(
            @"{installpath}\tools",
            @"C:\Acme"
        );

        result.Should().Be(@"C:\Acme\tools");
    }

    [Fact]
    public void ContainsEntry_ShouldFindExactMatch()
    {
        bool result = EnvironmentVariableService.ContainsEntry(
            @"C:\Windows;C:\Acme\bin;C:\Other",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsEntry_ShouldBeCaseInsensitive()
    {
        bool result = EnvironmentVariableService.ContainsEntry(
            @"C:\Windows;C:\ACME\BIN",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsEntry_ShouldNotMatchPartial()
    {
        bool result = EnvironmentVariableService.ContainsEntry(
            @"C:\Acme\bin2;C:\Other",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().BeFalse();
    }

    [Fact]
    public void RemoveEntry_ShouldRemoveExactMatch()
    {
        string result = EnvironmentVariableService.RemoveEntry(
            @"C:\Windows;C:\Acme\bin;C:\Other",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().Be(@"C:\Windows;C:\Other");
    }

    [Fact]
    public void RemoveEntry_ShouldBeCaseInsensitive()
    {
        string result = EnvironmentVariableService.RemoveEntry(
            @"C:\Windows;C:\ACME\BIN;C:\Other",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().Be(@"C:\Windows;C:\Other");
    }

    [Fact]
    public void RemoveEntry_ShouldHandleFirstEntry()
    {
        string result = EnvironmentVariableService.RemoveEntry(
            @"C:\Acme\bin;C:\Windows;C:\Other",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().Be(@"C:\Windows;C:\Other");
    }

    [Fact]
    public void RemoveEntry_ShouldHandleLastEntry()
    {
        string result = EnvironmentVariableService.RemoveEntry(
            @"C:\Windows;C:\Acme\bin",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().Be(@"C:\Windows");
    }

    [Fact]
    public void RemoveEntry_ShouldHandleSingleEntry()
    {
        string result = EnvironmentVariableService.RemoveEntry(@"C:\Acme\bin", @"C:\Acme\bin", ";");

        result.Should().BeEmpty();
    }

    [Fact]
    public void RemoveEntry_ShouldNotModifyWhenNotPresent()
    {
        string result = EnvironmentVariableService.RemoveEntry(
            @"C:\Windows;C:\Other",
            @"C:\Acme\bin",
            ";"
        );

        result.Should().Be(@"C:\Windows;C:\Other");
    }
}
