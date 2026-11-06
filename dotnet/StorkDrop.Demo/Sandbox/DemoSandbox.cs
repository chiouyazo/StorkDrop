using System.IO;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Demo.Sandbox;

/// <summary>
/// Redirects every StorkPaths location to a per-run branding folder and keeps all demo installs
/// below it, so the demo never reads or writes the real StorkDrop data of the user.
/// </summary>
internal static class DemoSandbox
{
    private const string PrefixStem = "demo";

    private static bool _initialized;

    public static string Prefix { get; private set; } = string.Empty;

    public static string AppsRoot => Path.Combine(StorkPaths.TempDir, "apps");

    public static string PluginsRoot => Path.Combine(AppsRoot, "Plugins");

    public static string NewFeedMarkerFile => Path.Combine(StorkPaths.ConfigDir, "config.json");

    public static void Initialize(string? prefix = null)
    {
        if (_initialized)
            return;

        Prefix = prefix ?? PrefixStem + Guid.NewGuid().ToString("N")[..8];
        Branding.Initialize(new BrandingInfo(Prefix: Prefix));
        Directory.CreateDirectory(AppsRoot);
        Directory.CreateDirectory(StorkPaths.ConfigDir);
        _initialized = true;
    }

    /// <summary>Maps any requested install path into the sandbox so nothing is ever written elsewhere.</summary>
    public static string Contain(string requestedPath)
    {
        string full;
        try
        {
            full = Path.GetFullPath(requestedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            full = Path.Combine(AppsRoot, "invalid-path");
            return full;
        }

        string root = Path.TrimEndingDirectorySeparator(AppsRoot) + Path.DirectorySeparatorChar;
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return full;

        char[] invalid = Path.GetInvalidFileNameChars();
        string flattened = string.Concat(full.Select(c => invalid.Contains(c) ? '_' : c));
        return Path.Combine(AppsRoot, "redirected", flattened);
    }

    public static void Cleanup()
    {
        if (!_initialized)
            return;

        string folder = Branding.Current.AppFolderName;
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetTempPath(),
        ];

        if (!folder.StartsWith(PrefixStem, StringComparison.Ordinal))
            return;

        foreach (string root in roots)
        {
            string path = Path.Combine(root, folder);
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }
    }
}
