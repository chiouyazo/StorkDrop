using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Tests.Installer;

public sealed class CleanupProbePlugin : IStorkPlugin
{
    public static string MarkerPath(string productId) =>
        Path.Combine(Path.GetTempPath(), $"storkdrop-cleanup-probe-{productId}.marker");

    public static string InstancesMarkerPath(string productId) =>
        Path.Combine(Path.GetTempPath(), $"storkdrop-instances-probe-{productId}.marker");

    public IReadOnlyList<PluginConfigField> GetConfigurationSchema(PluginEnvironment environment) =>
        [
            new PluginConfigField
            {
                Key = "probe",
                Label = "Probe",
                FieldType = PluginFieldType.Text,
            },
        ];

    public Task<PluginPreInstallResult> PreInstallAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    )
    {
        int instances = context.GetInstalledInstances?.Invoke("foreign-product").Count ?? -1;
        File.WriteAllText(InstancesMarkerPath(context.ProductId), instances.ToString());
        return Task.FromResult(new PluginPreInstallResult());
    }

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
    )
    {
        File.WriteAllText(MarkerPath(context.ProductId), "post-uninstall");
        return Task.CompletedTask;
    }
}
