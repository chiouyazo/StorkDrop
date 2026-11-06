using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace StorkDrop.Contracts.Services;

internal static class AtomicFile
{
    private const int MoveAttempts = 8;

    private static readonly ConcurrentDictionary<string, object> CommitGates = new(
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    public static string BackupPath(string path) => path + ".bak";

    public static bool IsValidJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// With <c>keepBackup</c> the current file is first copied to <c>.bak</c>, but only when it is valid
    /// JSON, so a corrupt file never replaces the last good backup.
    /// </summary>
    public static async Task WriteAllTextAsync(
        string path,
        string content,
        bool keepBackup = false,
        CancellationToken cancellationToken = default
    )
    {
        byte[] bytes = Utf8NoBom.GetBytes(content);
        string tempPath = NewTempPath(path);
        try
        {
            await using (
                FileStream stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough | FileOptions.Asynchronous
                )
            )
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            Commit(tempPath, path, keepBackup);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static void WriteAllText(string path, string content, bool keepBackup = false)
    {
        byte[] bytes = Utf8NoBom.GetBytes(content);
        string tempPath = NewTempPath(path);
        try
        {
            using (
                FileStream stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough
                )
            )
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            Commit(tempPath, path, keepBackup);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Reads <paramref name="path"/>; when it exists but is not valid, falls back to the <c>.bak</c>.
    /// A missing file yields null (the backup is deliberately not used then). When neither is valid the
    /// invalid primary content is returned so the caller's parser reports the real error.
    /// </summary>
    public static async Task<string?> ReadAllTextAsync(
        string path,
        Func<string, bool>? isValid = null,
        CancellationToken cancellationToken = default
    )
    {
        isValid ??= IsValidJson;
        if (!File.Exists(path))
            return null;

        string primary = await ReadSharedAsync(path, cancellationToken);
        if (isValid(primary))
            return primary;

        string backupPath = BackupPath(path);
        if (File.Exists(backupPath))
        {
            string backup = await ReadSharedAsync(backupPath, cancellationToken);
            if (isValid(backup))
                return backup;
        }

        return primary;
    }

    public static string? ReadAllText(string path, Func<string, bool>? isValid = null)
    {
        isValid ??= IsValidJson;
        if (!File.Exists(path))
            return null;

        string primary = ReadShared(path);
        if (isValid(primary))
            return primary;

        string backupPath = BackupPath(path);
        if (File.Exists(backupPath))
        {
            string backup = ReadShared(backupPath);
            if (isValid(backup))
                return backup;
        }

        return primary;
    }

    private static string NewTempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void Commit(string tempPath, string path, bool keepBackup)
    {
        object gate = CommitGates.GetOrAdd(Path.GetFullPath(path), _ => new object());
        lock (gate)
        {
            if (keepBackup)
                PreserveBackup(path);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, path, overwrite: true);
                    return;
                }
                catch (Exception ex)
                    when (attempt < MoveAttempts
                        && (ex is IOException or UnauthorizedAccessException)
                        && File.Exists(tempPath)
                    )
                {
                    Thread.Sleep(25 * attempt);
                }
            }
        }
    }

    private static void PreserveBackup(string path)
    {
        if (!File.Exists(path))
            return;

        string backupPath = BackupPath(path);
        string backupTemp = NewTempPath(backupPath);
        try
        {
            if (!IsValidJson(ReadShared(path)))
                return;

            File.Copy(path, backupTemp, overwrite: true);
            File.Move(backupTemp, backupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The backup is an extra safety net; failing to refresh it must not block the save.
        }
        finally
        {
            TryDelete(backupTemp);
        }
    }

    private static string ReadShared(string path)
    {
        using FileStream stream = OpenShared(path, FileOptions.SequentialScan);
        using StreamReader reader = new StreamReader(stream, Utf8NoBom, true);
        return reader.ReadToEnd();
    }

    private static async Task<string> ReadSharedAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using FileStream stream = OpenShared(
            path,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using StreamReader reader = new StreamReader(stream, Utf8NoBom, true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static FileStream OpenShared(string path, FileOptions options) =>
        new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            options
        );

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
