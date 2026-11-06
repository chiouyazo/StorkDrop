using System.Reflection;
using System.Runtime.InteropServices;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Provides the stable identity of this StorkDrop installation for outbound feed reports:
/// a persisted per-machine GUID plus hostname, OS description, and StorkDrop version.
/// </summary>
internal static class MachineIdentity
{
    private static readonly object Gate = new object();
    private static string? _machineId;

    /// <summary>A stable GUID for this machine, generated and persisted on first use.</summary>
    public static string MachineId
    {
        get
        {
            if (_machineId is not null)
                return _machineId;

            lock (Gate)
            {
                _machineId ??= LoadOrCreateMachineId();
                return _machineId;
            }
        }
    }

    public static string Hostname => Environment.MachineName;

    public static string OperatingSystem => RuntimeInformation.OSDescription;

    public static string StorkDropVersion
    {
        get
        {
            Assembly asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string? informational =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational ?? asm.GetName().Version?.ToString() ?? "unknown";
        }
    }

    private static string LoadOrCreateMachineId()
    {
        try
        {
            string path = StorkPaths.MachineIdFile;
            string? existing = AtomicFile.ReadAllText(path, IsGuid)?.Trim();
            if (existing is not null && IsGuid(existing))
                return existing;

            string id = Guid.NewGuid().ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, id);
            return AtomicFile.ReadAllText(path, IsGuid)?.Trim() ?? id;
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    private static bool IsGuid(string content) => Guid.TryParse(content.Trim(), out _);
}
