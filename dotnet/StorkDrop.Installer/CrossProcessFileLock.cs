using System.Diagnostics;

namespace StorkDrop.Installer;

/// <summary>
/// Exclusive, cross-process lock for read-modify-write cycles on a state file. The lock is an OS file
/// handle without sharing, so it is not bound to a thread, works across await points and is released
/// by the OS when the holding process dies.
/// </summary>
internal static class CrossProcessFileLock
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    public static async Task<IDisposable> AcquireAsync(
        string protectedPath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        string lockPath = LockPathFor(protectedPath);
        string? directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        TimeSpan limit = timeout ?? DefaultTimeout;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileStream? stream = TryOpen(lockPath);
            if (stream is not null)
                return new Releaser(stream);

            if (stopwatch.Elapsed >= limit)
            {
                throw new IOException(
                    $"Timed out after {limit.TotalSeconds:0}s waiting for exclusive access to '{protectedPath}'. Another StorkDrop process is writing it."
                );
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public static IDisposable? TryAcquire(string protectedPath)
    {
        string lockPath = LockPathFor(protectedPath);
        string? directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        FileStream? stream = TryOpen(lockPath);
        return stream is null ? null : new Releaser(stream);
    }

    internal static string LockPathFor(string protectedPath) => protectedPath + ".lock";

    private static FileStream? TryOpen(string lockPath)
    {
        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException) when (File.Exists(lockPath))
        {
            return null;
        }
    }

    private sealed class Releaser(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }
}
