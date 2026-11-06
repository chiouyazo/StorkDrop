using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Serializes install, update, re-execute and uninstall per product instance across all StorkDrop
/// processes (GUI, CLI and the elevated child). The lock is an exclusive file handle, so it is not tied
/// to a thread, survives await points and is released by the OS if the holder dies.
/// </summary>
public static class ProductOperationLock
{
    private const string DefaultInstance = "default";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly AsyncLocal<ImmutableHashSet<string>?> HeldKeys =
        new AsyncLocal<ImmutableHashSet<string>?>();

    private static int _inheritedOwnerProcessId;

    internal static string? LockDirectoryOverride { get; set; }

    /// <summary>
    /// Declares that an elevated child was started by <paramref name="ownerProcessId"/>, which keeps
    /// holding the locks the child works under; the child then treats locks held by that process as its own.
    /// </summary>
    public static void InheritFromProcess(int ownerProcessId) =>
        Volatile.Write(ref _inheritedOwnerProcessId, ownerProcessId);

    public static ProductLockAcquisition TryAcquire(
        string productId,
        string? instanceId,
        string operation
    ) =>
        TryAcquire(
            LockDirectoryOverride ?? Path.Combine(StorkPaths.StorkConfigDir, "locks"),
            productId,
            instanceId,
            operation,
            Volatile.Read(ref _inheritedOwnerProcessId)
        );

    internal static ProductLockAcquisition TryAcquire(
        string lockDirectory,
        string productId,
        string? instanceId,
        string operation,
        int inheritedOwnerProcessId
    )
    {
        string key = KeyFor(productId, instanceId);
        ImmutableHashSet<string> held = HeldKeys.Value ?? ImmutableHashSet<string>.Empty;
        if (held.Contains(key))
            return new ProductLockAcquisition(true, null, null);

        Directory.CreateDirectory(lockDirectory);
        string baseName = FileNameFor(productId, instanceId, key);
        string lockPath = Path.Combine(lockDirectory, baseName + ".lock");
        string infoPath = Path.Combine(lockDirectory, baseName + ".info");

        FileStream? handle = TryOpen(lockPath);
        if (handle is null)
        {
            ProductLockHolder? holder = ReadHolder(infoPath);
            if (holder is not null && holder.ProcessId == inheritedOwnerProcessId)
            {
                HeldKeys.Value = held.Add(key);
                return new ProductLockAcquisition(true, holder, () => HeldKeys.Value = held);
            }

            return new ProductLockAcquisition(false, holder, null);
        }

        WriteHolder(infoPath, operation);
        HeldKeys.Value = held.Add(key);
        return new ProductLockAcquisition(
            true,
            null,
            () =>
            {
                HeldKeys.Value = held;
                TryDelete(infoPath);
                handle.Dispose();
            }
        );
    }

    internal static string KeyFor(string productId, string? instanceId) =>
        $"{productId}\n{(string.IsNullOrEmpty(instanceId) ? DefaultInstance : instanceId)}".ToLowerInvariant();

    private static string FileNameFor(string productId, string? instanceId, string key)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        string instance = string.IsNullOrEmpty(instanceId) ? DefaultInstance : instanceId;
        return $"{Sanitize(productId)}__{Sanitize(instance)}__{hash}";
    }

    private static string Sanitize(string value)
    {
        StringBuilder builder = new StringBuilder(Math.Min(value.Length, 40));
        foreach (char c in value)
        {
            if (builder.Length == 40)
                break;
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        }
        return builder.ToString();
    }

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

    private static void WriteHolder(string infoPath, string operation)
    {
        using Process current = Process.GetCurrentProcess();
        ProductLockHolder holder = new ProductLockHolder(
            current.Id,
            current.ProcessName,
            DateTimeOffset.UtcNow,
            operation
        );
        try
        {
            File.WriteAllText(infoPath, JsonSerializer.Serialize(holder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Holder details only improve the conflict message; the lock itself is already held.
        }
    }

    private static ProductLockHolder? ReadHolder(string infoPath)
    {
        try
        {
            if (!File.Exists(infoPath))
                return null;

            ProductLockHolder? holder = JsonSerializer.Deserialize<ProductLockHolder>(
                File.ReadAllText(infoPath),
                JsonOptions
            );
            return holder is not null && IsLive(holder) ? holder : null;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsLive(ProductLockHolder holder)
    {
        try
        {
            using Process process = Process.GetProcessById(holder.ProcessId);
            return string.Equals(
                process.ProcessName,
                holder.ProcessName,
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
