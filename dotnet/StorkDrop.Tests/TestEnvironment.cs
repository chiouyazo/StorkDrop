using System.Runtime.CompilerServices;
using StorkDrop.Installer;

namespace StorkDrop.Tests;

internal static class TestEnvironment
{
    private static readonly string LockDirectory = Path.Combine(
        Path.GetTempPath(),
        "StorkDropTests",
        "locks-" + Guid.NewGuid().ToString("N")
    );

    [ModuleInitializer]
    internal static void Initialize()
    {
        ProductOperationLock.LockDirectoryOverride = LockDirectory;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(LockDirectory))
                    Directory.Delete(LockDirectory, recursive: true);
            }
            catch (IOException) { }
        };
    }
}
