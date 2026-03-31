using System.Reflection;
using System.Runtime.InteropServices;
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
        string name = assemblyName.Name ?? "";

        if (HostAssemblies.Contains(name))
            return FindInHost(name);

        // Check runtimes/{rid}/lib/ for platform-specific managed assemblies (e.g., Microsoft.Data.SqlClient)
        Assembly? runtimeAssembly = LoadFromRuntimesDirectory(name);
        if (runtimeAssembly is not null)
            return runtimeAssembly;

        // Plugin's own dependencies from its root directory
        string path = Path.Combine(pluginDirectory, $"{name}.dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(path);

        // Fall back to host
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch
        {
            return FindInHost(name);
        }
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // Probe runtimes/{rid}/native/ for native libraries (e.g., Microsoft.Data.SqlClient.SNI)
        string rid = RuntimeInformation.RuntimeIdentifier;
        string? nativePath = FindNativeLibrary(unmanagedDllName, rid);
        if (nativePath is not null)
            return NativeLibrary.Load(nativePath);

        // Try architecture-specific RID (e.g., win-x64)
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string osRid = $"win-{arch}";
        if (osRid != rid)
        {
            nativePath = FindNativeLibrary(unmanagedDllName, osRid);
            if (nativePath is not null)
                return NativeLibrary.Load(nativePath);
        }

        return IntPtr.Zero;
    }

    private Assembly? LoadFromRuntimesDirectory(string assemblyName)
    {
        string runtimesDir = Path.Combine(pluginDirectory, "runtimes");
        if (!Directory.Exists(runtimesDir))
            return null;

        // Prefer win (managed) then win-{arch}
        string[] rids =
        [
            "win",
            $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}",
            RuntimeInformation.RuntimeIdentifier,
        ];

        foreach (string rid in rids)
        {
            string ridLibDir = Path.Combine(runtimesDir, rid, "lib");
            if (!Directory.Exists(ridLibDir))
                continue;

            foreach (string tfmDir in Directory.GetDirectories(ridLibDir))
            {
                string dllPath = Path.Combine(tfmDir, $"{assemblyName}.dll");
                if (File.Exists(dllPath))
                    return LoadFromAssemblyPath(dllPath);
            }
        }

        return null;
    }

    private string? FindNativeLibrary(string name, string rid)
    {
        string nativeDir = Path.Combine(pluginDirectory, "runtimes", rid, "native");
        if (!Directory.Exists(nativeDir))
            return null;

        string path = Path.Combine(nativeDir, $"{name}.dll");
        if (File.Exists(path))
            return path;

        path = Path.Combine(nativeDir, name);
        if (File.Exists(path))
            return path;

        return null;
    }

    private static Assembly? FindInHost(string name)
    {
        foreach (Assembly loaded in Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }
        return null;
    }
}
