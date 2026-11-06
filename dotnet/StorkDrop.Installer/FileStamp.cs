namespace StorkDrop.Installer;

internal readonly record struct FileStamp(long LastWriteTicks, long Length)
{
    public static FileStamp Of(string path)
    {
        FileInfo info = new FileInfo(path);
        return info.Exists
            ? new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length)
            : new FileStamp(0, -1);
    }
}
