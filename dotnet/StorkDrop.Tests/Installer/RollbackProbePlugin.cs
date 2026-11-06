using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Tests.Installer;

public sealed class RollbackProbePlugin : IStorkPlugin, IRollbackAwarePlugin
{
    public static string MarkerPath(string productId) =>
        Path.Combine(Path.GetTempPath(), $"storkdrop-rollback-probe-{productId}.marker");

    public IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment environment) =>
        [];

    public Task<PluginPreInstallResult> PreInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new PluginPreInstallResult());

    public Task PostInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task<PluginPreInstallResult> PreUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new PluginPreInstallResult());

    public Task PostUninstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task PrepareForRollbackAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    )
    {
        File.WriteAllText(MarkerPath(context.ProductId), context.Version);
        return Task.CompletedTask;
    }
}
