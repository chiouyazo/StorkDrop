using System.Diagnostics;
using System.Security.Principal;

namespace StorkDrop.Contracts.Services;

public static class ElevationHelper
{
    public static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool PathRequiresAdmin(string path)
    {
        string normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        string programFiles = Environment
            .GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            .ToLowerInvariant();
        string programFilesX86 = Environment
            .GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            .ToLowerInvariant();
        string windows = Environment
            .GetFolderPath(Environment.SpecialFolder.Windows)
            .ToLowerInvariant();

        return normalizedPath.StartsWith(programFiles)
            || normalizedPath.StartsWith(programFilesX86)
            || normalizedPath.StartsWith(windows);
    }

    public static bool RunElevatedInstall(
        string productId,
        string version,
        string targetPath,
        string feedId
    )
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return false;

            string pluginDirArgs = GetPluginDirArgs();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments =
                    $"--install \"{productId}\" \"{targetPath}\" \"{feedId}\" {pluginDirArgs}",
            };

            Process? process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit(TimeSpan.FromMinutes(10));
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool RunElevatedUninstall(string productId)
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return false;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--uninstall \"{productId}\" {GetPluginDirArgs()}",
            };

            Process? process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit(TimeSpan.FromMinutes(10));
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool RunElevatedUpdate(string productId, string targetPath, string feedId)
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return false;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments =
                    $"--update \"{productId}\" \"{targetPath}\" \"{feedId}\" {GetPluginDirArgs()}",
            };

            Process? process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit(TimeSpan.FromMinutes(10));
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartAsAdmin(string[]? args = null)
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return false;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = args is not null ? string.Join(" ", args) : string.Empty,
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Collects --plugin-dir arguments from the current process to forward to elevated processes.
    /// </summary>
    private static string GetPluginDirArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        List<string> pluginDirs = [];
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--plugin-dir")
                pluginDirs.Add($"--plugin-dir \"{args[i + 1]}\"");
        }
        return string.Join(" ", pluginDirs);
    }
}
