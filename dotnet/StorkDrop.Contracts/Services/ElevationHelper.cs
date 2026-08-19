using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace StorkDrop.Contracts.Services;

public static class ElevationHelper
{
    // Win32 ERROR_CANCELLED: ShellExecute "runas" throws this when the user declines the UAC request.
    private const int ErrorCancelled = 1223;

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

    public static ElevationResult RunElevatedInstall(
        string productId,
        string version,
        string targetPath,
        string feedId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        string? configFilePath = null,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return ElevationResult.Failed;

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

            return WaitForElevatedProcess(
                startInfo,
                onProgressLine,
                onLongRunning,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ElevationResult.DeniedByUser;
        }
        catch
        {
            return ElevationResult.Failed;
        }
    }

    public static ElevationResult RunElevatedUninstall(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return ElevationResult.Failed;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments =
                    $"--uninstall \"{productId}\" --instance \"{instanceId}\" {GetPluginDirArgs()}".Trim(),
            };

            return WaitForElevatedProcess(
                startInfo,
                onProgressLine,
                onLongRunning,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ElevationResult.DeniedByUser;
        }
        catch
        {
            return ElevationResult.Failed;
        }
    }

    public static ElevationResult RunElevatedUpdate(
        string productId,
        string targetPath,
        string feedId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        string? configFilePath = null,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return ElevationResult.Failed;

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

            return WaitForElevatedProcess(
                startInfo,
                onProgressLine,
                onLongRunning,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ElevationResult.DeniedByUser;
        }
        catch
        {
            return ElevationResult.Failed;
        }
    }

    public static ElevationResult RunElevatedReExecute(
        string productId,
        string instanceId,
        bool runPreInstall,
        bool runPostInstall,
        string? configFilePath = null,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return ElevationResult.Failed;

            string configFileArg = configFilePath is not null
                ? $"--config-file \"{configFilePath}\""
                : "";
            string skipPre = runPreInstall ? "" : "--skip-pre";
            string skipPost = runPostInstall ? "" : "--skip-post";
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments =
                    $"--reexecute \"{productId}\" --instance \"{instanceId}\" {skipPre} {skipPost} {GetPluginDirArgs()} {configFileArg}".Trim(),
            };

            return WaitForElevatedProcess(
                startInfo,
                onProgressLine,
                onLongRunning,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ElevationResult.DeniedByUser;
        }
        catch
        {
            return ElevationResult.Failed;
        }
    }

    // Timeout after which onLongRunning is consulted before killing the child.
    private static readonly TimeSpan LongRunningThreshold = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Starts an elevated child process and waits for it to exit, surfacing progress by tailing
    /// the shared StorkDrop rolling log file (stdout cannot be redirected under <c>runas</c>).
    /// </summary>
    /// <param name="startInfo">The elevated child process to start.</param>
    /// <param name="onProgressLine">Invoked for each new non-empty log line appended while waiting.</param>
    /// <param name="onLongRunning">
    /// Invoked with the last observed log line when the process has run past the long-running
    /// threshold without exiting. Return <c>true</c> to keep waiting, <c>false</c> to kill it.
    /// When null, a long-running process is killed.
    /// </param>
    /// <param name="cancellationToken">
    /// When cancellation is requested the elevated child is killed and an
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </param>
    /// <returns>
    /// <see cref="ElevationResult.Succeeded"/> only if the process exited with code 0; otherwise
    /// <see cref="ElevationResult.Failed"/>. A child that never started or exited non-zero is a
    /// failure, not a refusal - a refusal is caught as a <see cref="Win32Exception"/> by the caller.
    /// </returns>
    private static ElevationResult WaitForElevatedProcess(
        ProcessStartInfo startInfo,
        Action<string>? onProgressLine,
        Func<string, bool>? onLongRunning,
        CancellationToken cancellationToken = default
    )
    {
        // Determine the log file to tail and its current end position BEFORE starting the child,
        // so we only surface lines produced by the elevated install.
        string? logPath = GetNewestLogFile();
        long position = 0;
        if (logPath is not null)
        {
            try
            {
                position = new FileInfo(logPath).Length;
            }
            catch
            {
                position = 0;
            }
        }

        Process? process = Process.Start(startInfo);
        if (process is null)
            return ElevationResult.Failed;

        string currentStep = string.Empty;
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (!process.WaitForExit(1000))
        {
            (position, currentStep) = TailLog(logPath, position, currentStep, onProgressLine);

            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort; the process may have exited in the meantime.
                }
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (stopwatch.Elapsed >= LongRunningThreshold)
            {
                bool keepWaiting = onLongRunning?.Invoke(currentStep) ?? false;
                if (!keepWaiting)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Best effort; the process may have exited in the meantime.
                    }
                    return ElevationResult.Failed;
                }

                stopwatch.Restart();
            }
        }

        // Flush any lines appended just before exit.
        TailLog(logPath, position, currentStep, onProgressLine);
        return process.ExitCode == 0 ? ElevationResult.Succeeded : ElevationResult.Failed;
    }

    /// <summary>
    /// Reads bytes appended to <paramref name="logPath"/> since <paramref name="position"/>, forwards
    /// each new non-empty line via <paramref name="onProgressLine"/>, and returns the updated position
    /// and last non-empty line seen.
    /// </summary>
    private static (long Position, string CurrentStep) TailLog(
        string? logPath,
        long position,
        string currentStep,
        Action<string>? onProgressLine
    )
    {
        if (logPath is null)
            return (position, currentStep);

        try
        {
            using FileStream stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            if (position > stream.Length)
                position = 0; // File was rolled/truncated; restart from the beginning.

            stream.Seek(position, SeekOrigin.Begin);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            string content = reader.ReadToEnd();
            position = stream.Position;

            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0)
                    continue;

                currentStep = line;
                onProgressLine?.Invoke(line);
            }
        }
        catch
        {
            // Log unavailable this tick; try again next poll.
        }

        return (position, currentStep);
    }

    /// <summary>Finds the most recently written <c>storkdrop-*.log</c> in the log directory, if any.</summary>
    private static string? GetNewestLogFile()
    {
        try
        {
            string dir = StorkPaths.LogDir;
            if (!Directory.Exists(dir))
                return null;

            return new DirectoryInfo(dir)
                .GetFiles("storkdrop-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
        }
        catch
        {
            return null;
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
