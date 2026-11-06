using System.IO.Compression;

namespace StorkDrop.Installer;

internal static class DiskSpaceGuard
{
    public static long? GetAvailableBytes(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return null;

            DriveInfo drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex)
            when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void EnsureFreeSpace(
        string path,
        long requiredBytes,
        string purpose,
        Func<string, long?>? availableBytes = null
    )
    {
        long? available = (availableBytes ?? GetAvailableBytes)(path);
        if (available is long free && free < requiredBytes)
        {
            throw new InsufficientDiskSpaceException(
                Path.GetPathRoot(Path.GetFullPath(path)) ?? path,
                purpose,
                requiredBytes,
                free
            );
        }
    }

    public static long UncompressedSize(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Sum(entry => entry.Length);
    }
}
