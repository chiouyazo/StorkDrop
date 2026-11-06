using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace StorkDrop.Installer;

internal static class StartMenuShortcutCleaner
{
    public static int RemoveProductShortcuts(
        string programsFolder,
        string productTitle,
        string installPath,
        Func<string, (string? Target, string? WorkingDirectory)> readShortcut
    )
    {
        if (!Directory.Exists(programsFolder) || string.IsNullOrWhiteSpace(installPath))
            return 0;

        int removed = 0;
        foreach (string subDir in Directory.GetDirectories(programsFolder))
        {
            try
            {
                bool removedFromFolder = false;
                foreach (string lnk in Directory.GetFiles(subDir, "*.lnk"))
                {
                    if (
                        !Path.GetFileNameWithoutExtension(lnk)
                            .Contains(productTitle, StringComparison.OrdinalIgnoreCase)
                    )
                        continue;

                    (string? target, string? workingDirectory) = readShortcut(lnk);
                    if (!IsInside(target, installPath) && !IsInside(workingDirectory, installPath))
                        continue;

                    File.Delete(lnk);
                    removed++;
                    removedFromFolder = true;
                }

                if (
                    removedFromFolder
                    && Directory.GetFiles(subDir).Length == 0
                    && Directory.GetDirectories(subDir).Length == 0
                )
                    Directory.Delete(subDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return removed;
    }

    public static (string? Target, string? WorkingDirectory) ReadWithShell(string linkPath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            return (null, null);

        try
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(linkPath);
            return ((string?)link.TargetPath, (string?)link.WorkingDirectory);
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException)
        {
            return (null, null);
        }
    }

    internal static bool IsInside(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );
    }
}
