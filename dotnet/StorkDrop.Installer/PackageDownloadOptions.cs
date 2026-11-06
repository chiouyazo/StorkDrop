namespace StorkDrop.Installer;

internal sealed class PackageDownloadOptions
{
    public int MaxAttempts { get; init; } = 4;

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public long DiskReserveBytes { get; init; } = 16L * 1024 * 1024;

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public Func<string, long?> AvailableBytes { get; init; } = DiskSpaceGuard.GetAvailableBytes;
}
