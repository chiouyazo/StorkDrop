namespace StorkDrop.Contracts.Services;

/// <summary>
/// Provides formatting utilities for display values.
/// </summary>
public static class FormatHelper
{
    private static readonly string[] ByteSuffixes = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g., "1.5 GB").
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string with an appropriate unit suffix.</returns>
    public static string FormatBytes(long bytes)
    {
        double size = bytes;
        int suffixIndex = 0;

        while (size >= 1024 && suffixIndex < ByteSuffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.##} {ByteSuffixes[suffixIndex]}";
    }

    /// <summary>
    /// Gets the available free space on the drive containing the specified path.
    /// </summary>
    /// <param name="path">A file or directory path on the target drive.</param>
    /// <returns>The formatted available space, or null if it cannot be determined.</returns>
    public static string? GetFormattedDiskSpace(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return null;

        DriveInfo driveInfo = new DriveInfo(root);
        return driveInfo.IsReady ? FormatBytes(driveInfo.AvailableFreeSpace) : null;
    }
}
