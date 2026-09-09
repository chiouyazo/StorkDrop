using System.Reflection;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// The running StorkDrop host's own version, used to check a product's declared
/// <see cref="Models.ProductManifest.MinHostVersion"/> before running its plugins. Because
/// <c>StorkDrop.Contracts</c> is resolved from the host (not the plugin), a product built against a
/// newer contract surface would otherwise fail deep inside a plugin with an opaque
/// <see cref="MissingMethodException"/>.
/// </summary>
public static class HostVersion
{
    /// <summary>The host version string (e.g. "1.1.83"), build metadata stripped.</summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        Assembly host = Assembly.GetEntryAssembly() ?? typeof(HostVersion).Assembly;

        string? informational =
            host.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return host.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
