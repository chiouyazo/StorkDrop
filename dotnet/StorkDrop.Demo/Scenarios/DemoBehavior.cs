namespace StorkDrop.Demo.Scenarios;

internal sealed record DemoBehavior
{
    public bool SlowUpdate { get; init; }
    public int TransientDownloadFailures { get; init; }
    public bool DownloadAlwaysFails { get; init; }
    public bool ChecksumMismatch { get; init; }
    public bool DiskFull { get; init; }
    public string? PreInstallError { get; init; }
    public string? PostInstallError { get; init; }
    public bool ElevationSlow { get; init; }
    public bool SkipConfigDialog { get; init; }
    public bool RebootOnUninstall { get; init; }
}
