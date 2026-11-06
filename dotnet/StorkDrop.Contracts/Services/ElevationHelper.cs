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
    ) =>
        RunElevated(
            progressLog =>
                ElevationArguments.ForInstall(
                    productId,
                    version,
                    targetPath,
                    feedId,
                    instanceId,
                    configFilePath,
                    Environment.GetCommandLineArgs(),
                    Environment.ProcessId,
                    progressLog
                ),
            onProgressLine,
            onLongRunning,
            cancellationToken
        );

    public static ElevationResult RunElevatedUninstall(
        string productId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    ) =>
        RunElevated(
            progressLog =>
                ElevationArguments.ForUninstall(
                    productId,
                    instanceId,
                    Environment.GetCommandLineArgs(),
                    Environment.ProcessId,
                    progressLog
                ),
            onProgressLine,
            onLongRunning,
            cancellationToken
        );

    public static ElevationResult RunElevatedUpdate(
        string productId,
        string version,
        string targetPath,
        string feedId,
        string instanceId = InstanceIdHelper.DefaultInstanceId,
        string? configFilePath = null,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    ) =>
        RunElevated(
            progressLog =>
                ElevationArguments.ForUpdate(
                    productId,
                    version,
                    targetPath,
                    feedId,
                    instanceId,
                    configFilePath,
                    Environment.GetCommandLineArgs(),
                    Environment.ProcessId,
                    progressLog
                ),
            onProgressLine,
            onLongRunning,
            cancellationToken
        );

    public static ElevationResult RunElevatedReExecute(
        string productId,
        string instanceId,
        bool runPreInstall,
        bool runPostInstall,
        string? configFilePath = null,
        Action<string>? onProgressLine = null,
        Func<string, bool>? onLongRunning = null,
        CancellationToken cancellationToken = default
    ) =>
        RunElevated(
            progressLog =>
                ElevationArguments.ForReExecute(
                    productId,
                    instanceId,
                    runPreInstall,
                    runPostInstall,
                    configFilePath,
                    Environment.GetCommandLineArgs(),
                    Environment.ProcessId,
                    progressLog
                ),
            onProgressLine,
            onLongRunning,
            cancellationToken
        );

    private static ElevationResult RunElevated(
        Func<string, string> buildArguments,
        Action<string>? onProgressLine,
        Func<string, bool>? onLongRunning,
        CancellationToken cancellationToken
    )
    {
        string? progressLog = null;
        try
        {
            string exePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                return ElevationResult.Failed;

            Directory.CreateDirectory(StorkPaths.TempDir);
            progressLog = Path.Combine(
                StorkPaths.TempDir,
                $"{ProgressLogPrefix}{Guid.NewGuid():N}.log"
            );
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = buildArguments(progressLog),
            };

            return WaitForElevatedProcess(
                startInfo,
                progressLog,
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
        finally
        {
            DeleteQuietly(progressLog);
        }
    }

    internal const string ProgressLogPrefix = "elevation-progress-";

    // Interval after which onLongRunning is consulted. Without a callback the child is never killed.
    private static readonly TimeSpan LongRunningThreshold = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Starts an elevated child process and waits for it to exit, surfacing progress by tailing the
    /// child's dedicated progress log (stdout cannot be redirected under <c>runas</c>).
    /// </summary>
    private static ElevationResult WaitForElevatedProcess(
        ProcessStartInfo startInfo,
        string progressLogPath,
        Action<string>? onProgressLine,
        Func<string, bool>? onLongRunning,
        CancellationToken cancellationToken
    )
    {
        using Process? process = Process.Start(startInfo);
        if (process is null)
            return ElevationResult.Failed;

        return WaitForChild(
            new ProcessElevatedChild(process),
            progressLogPath,
            onProgressLine,
            onLongRunning,
            LongRunningThreshold,
            PollInterval,
            cancellationToken
        );
    }

    /// <param name="child">The elevated child process to wait for.</param>
    /// <param name="progressLogPath">Log file written only by this child; each new line is forwarded.</param>
    /// <param name="onProgressLine">Invoked for each new non-empty log line appended while waiting.</param>
    /// <param name="onLongRunning">
    /// Consulted with the last observed log line each time <paramref name="longRunningThreshold"/>
    /// elapses without the child exiting. Return <c>true</c> to keep waiting, <c>false</c> to kill it.
    /// When null, a running child is never killed because of elapsed time.
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
    internal static ElevationResult WaitForChild(
        IElevatedChild child,
        string? progressLogPath,
        Action<string>? onProgressLine,
        Func<string, bool>? onLongRunning,
        TimeSpan longRunningThreshold,
        TimeSpan pollInterval,
        CancellationToken cancellationToken
    )
    {
        long position = 0;
        string currentStep = string.Empty;
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (!child.WaitForExit(pollInterval))
        {
            (position, currentStep) = TailLog(
                progressLogPath,
                position,
                currentStep,
                onProgressLine
            );

            if (cancellationToken.IsCancellationRequested)
            {
                child.Kill();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (stopwatch.Elapsed >= longRunningThreshold)
            {
                if (onLongRunning is not null && !onLongRunning(currentStep))
                {
                    child.Kill();
                    return ElevationResult.Failed;
                }

                stopwatch.Restart();
            }
        }

        TailLog(progressLogPath, position, currentStep, onProgressLine);
        return child.ExitCode == 0 ? ElevationResult.Succeeded : ElevationResult.Failed;
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

    private static void DeleteQuietly(string? path)
    {
        if (path is null)
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
                Arguments = args is not null ? ElevationArguments.Join(args) : string.Empty,
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
