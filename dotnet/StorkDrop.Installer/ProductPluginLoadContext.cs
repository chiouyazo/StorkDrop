using System.Reflection;
using System.Runtime.Loader;

namespace StorkDrop.Installer;

internal sealed class ProductPluginLoadContext(string pluginDirectory)
    : AssemblyLoadContext(isCollectible: true)
{
    private static readonly HashSet<string> HostAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "StorkDrop.Contracts",
    };

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Shared assemblies always come from the host to avoid type identity issues
        if (HostAssemblies.Contains(assemblyName.Name ?? ""))
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                return null;
            }
        }

        // Plugin's own dependencies from its directory
        string path = Path.Combine(pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(path);

        // Fall back to host for anything else
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch
        {
            return null;
        }
    }
}
