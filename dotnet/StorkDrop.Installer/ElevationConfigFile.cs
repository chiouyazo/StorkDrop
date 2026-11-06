using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Hands plugin configuration values (which may contain secrets) to the elevated child through a file
/// that only the current user, administrators and SYSTEM can read, and removes it reliably.
/// </summary>
public static class ElevationConfigFile
{
    private const string ConfigPrefix = "elevation-config-";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(4);

    public static Task<string> WriteAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken
    ) => WriteAsync(values, StorkPaths.TempDir, cancellationToken);

    internal static async Task<string> WriteAsync(
        IReadOnlyDictionary<string, string> values,
        string directory,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{ConfigPrefix}{Guid.NewGuid()}.json");
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values));

        bool written = false;
        try
        {
            await using (
                FileStream stream = new FileInfo(path).Create(
                    FileMode.CreateNew,
                    FileSystemRights.WriteData | FileSystemRights.ReadAttributes,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous,
                    RestrictedSecurity()
                )
            )
            {
                await stream.WriteAsync(bytes, cancellationToken);
            }
            written = true;
            return path;
        }
        finally
        {
            if (!written)
                TryDelete(path);
        }
    }

    public static Dictionary<string, string>? ReadAndDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void CleanupStale() => CleanupStale(StorkPaths.TempDir, StaleAfter);

    internal static void CleanupStale(string directory, TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            foreach (
                string pattern in new[]
                {
                    $"{ConfigPrefix}*.json",
                    $"{ElevationHelper.ProgressLogPrefix}*.log",
                }
            )
            {
                foreach (string file in Directory.GetFiles(directory, pattern))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > maxAge)
                        TryDelete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static FileSecurity RestrictedSecurity()
    {
        FileSecurity security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
            Allow(security, user);
        Allow(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        Allow(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        return security;
    }

    private static void Allow(FileSecurity security, SecurityIdentifier identity) =>
        security.AddAccessRule(
            new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                AccessControlType.Allow
            )
        );
}
