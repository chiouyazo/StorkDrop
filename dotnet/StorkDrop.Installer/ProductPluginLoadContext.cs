using System.Reflection;
using System.Runtime.Loader;

namespace StorkDrop.Installer;

internal sealed class ProductPluginLoadContext(string pluginDirectory)
    : AssemblyLoadContext(isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string path = Path.Combine(pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(path);

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
