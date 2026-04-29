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
        // Quick check for well-known protected paths
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

        if (
            normalizedPath.StartsWith(programFiles)
            || normalizedPath.StartsWith(programFilesX86)
            || normalizedPath.StartsWith(windows)
        )
            return true;

        // Probe actual write access -> handles e.g. C:\Users\Default, ACL-restricted folders, etc.
        try
        {
            // Find the deepest existing directory in the path
            string? testDir = Path.GetFullPath(path);
            while (testDir is not null && !Directory.Exists(testDir))
                testDir = Path.GetDirectoryName(testDir);

            if (testDir is null)
                return true;

            string testFile = Path.Combine(testDir, $".storkdrop-write-test-{Guid.NewGuid()}");
            using (File.Create(testFile)) { }
            File.Delete(testFile);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public static bool RunElevatedInstall(
        string productId,
        string version,
        string targetPath,
        string feedId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        string? configFilePath = null
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
            string configFileArg = configFilePath is not null
                ? $"--config-file \"{configFilePath}\""
                : "";
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments =
                    $"--install \"{productId}\" \"{targetPath}\" \"{feedId}\" --instance \"{instanceId}\" {pluginDirArgs} {configFileArg}".Trim(),
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

    public static bool RunElevatedUninstall(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId
    )
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
                    $"--uninstall \"{productId}\" --instance \"{instanceId}\" {GetPluginDirArgs()}".Trim(),
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

    public static bool RunElevatedUpdate(
        string productId,
        string targetPath,
        string feedId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        string? configFilePath = null
    )
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return false;

            string configFileArg = configFilePath is not null
                ? $"--config-file \"{configFilePath}\""
                : "";
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments =
                    $"--update \"{productId}\" \"{targetPath}\" \"{feedId}\" --instance \"{instanceId}\" {GetPluginDirArgs()} {configFileArg}".Trim(),
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
