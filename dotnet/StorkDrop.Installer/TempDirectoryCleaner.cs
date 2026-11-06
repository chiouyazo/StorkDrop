namespace StorkDrop.Installer;

public static class TempDirectoryCleaner
{
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Deletes direct subdirectories of <paramref name="root"/> that have not been touched for at
    /// least <paramref name="minimumAge"/>. Younger folders may belong to an install that is still
    /// running in another process. Returns the number of deleted directories.
    /// </summary>
    public static int DeleteStaleSubdirectories(
        string root,
        TimeSpan minimumAge,
        DateTime? utcNow = null
    )
    {
        if (!Directory.Exists(root))
            return 0;

        DateTime threshold = (utcNow ?? DateTime.UtcNow) - minimumAge;
        int deleted = 0;
        foreach (string directory in Directory.GetDirectories(root))
        {
            try
            {
                if (GetLastActivityUtc(directory) > threshold)
                    continue;

                Directory.Delete(directory, recursive: true);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return deleted;
    }

    private static DateTime GetLastActivityUtc(string directory)
    {
        DateTime latest = Directory.GetLastWriteTimeUtc(directory);
        foreach (
            string entry in Directory.EnumerateFileSystemEntries(
                directory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            DateTime entryTime = File.GetLastWriteTimeUtc(entry);
            if (entryTime > latest)
                latest = entryTime;
        }

        return latest;
    }
}
