using System.Diagnostics;
using System.IO;
using StorkDrop.Demo.Scenarios;
using StorkDrop.Installer;

namespace StorkDrop.Demo.Sandbox;

/// <summary>
/// Holds the real cross-process operation locks of two demo products from a second process, so the
/// real coordinator reports a genuine conflict with a foreign process name and PID.
/// </summary>
internal static class DemoLockHolder
{
    public const string Argument = "--hold-locks";

    private static readonly string[] LockedProducts =
    [
        DemoIds.UpdateLockConflict,
        DemoIds.InstallLockConflict,
    ];

    public static bool TryRunAsHolder(string[] args)
    {
        if (args.Length < 3 || args[0] != Argument)
            return false;

        DemoSandbox.Initialize(args[1]);
        List<ProductLockAcquisition> held = [];
        foreach (string productId in LockedProducts)
            held.Add(ProductOperationLock.TryAcquire(productId, "default", "install"));

        try
        {
            using Process parent = Process.GetProcessById(int.Parse(args[2]));
            parent.WaitForExit();
        }
        catch (ArgumentException) { }

        foreach (ProductLockAcquisition acquisition in held)
            acquisition.Dispose();
        return true;
    }

    public static Process? Start()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null)
            return null;

        ProcessStartInfo info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (
            Path.GetFileNameWithoutExtension(exe)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        )
            info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);

        info.ArgumentList.Add(Argument);
        info.ArgumentList.Add(DemoSandbox.Prefix);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        return Process.Start(info);
    }
}
