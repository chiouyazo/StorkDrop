using FluentAssertions;
using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class InstallPathResolutionTests
{
    private sealed class TokenResolver(string token, string value) : IInstallPathResolver
    {
        public string? ResolveInstallPath(string targetPath, PluginContext? context) =>
            targetPath.Contains(token) ? targetPath.Replace(token, value) : null;
    }

    private sealed class ConfigResolver(string token, string configKey) : IInstallPathResolver
    {
        public string? ResolveInstallPath(string targetPath, PluginContext? context)
        {
            if (!targetPath.Contains(token) || context is null)
                return null;
            return context.ConfigValues.TryGetValue(configKey, out string? value)
                ? targetPath.Replace(token, value)
                : null;
        }
    }

    [Fact]
    public void Apply_WithNoResolvers_ReturnsPathUnchanged()
    {
        InstallPathResolution.Apply("C:/x/{Steps}", [], null).Should().Be("C:/x/{Steps}");
    }

    [Fact]
    public void Apply_ResolverRewritesToken()
    {
        string result = InstallPathResolution.Apply(
            "{Steps}/app",
            [new TokenResolver("{Steps}", "D:/Steps")],
            null
        );

        result.Should().Be("D:/Steps/app");
    }

    [Fact]
    public void Apply_FirstResolverWins_LaterOnesSeeNoToken()
    {
        // The product resolver (first) resolves the token; the host resolver (second) then sees none.
        IInstallPathResolver[] resolvers =
        [
            new TokenResolver("{Steps}", "D:/Product"),
            new TokenResolver("{Steps}", "D:/Host"),
        ];

        InstallPathResolution.Apply("{Steps}/x", resolvers, null).Should().Be("D:/Product/x");
    }

    [Fact]
    public void Apply_ResolvesFromTheProductsOwnConfig()
    {
        PluginContext context = new() { ConfigValues = { ["steps-dir"] = "E:/Chosen" } };

        string result = InstallPathResolution.Apply(
            "{Steps}/y",
            [new ConfigResolver("{Steps}", "steps-dir")],
            context
        );

        result.Should().Be("E:/Chosen/y");
    }
}
