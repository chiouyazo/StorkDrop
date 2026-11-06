using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public sealed class InsufficientDiskSpaceException : IOException
{
    public InsufficientDiskSpaceException(
        string root,
        string purpose,
        long requiredBytes,
        long availableBytes
    )
        : base(
            $"Not enough free disk space on {root} for {purpose}: {FormatHelper.FormatBytes(requiredBytes)} required, {FormatHelper.FormatBytes(availableBytes)} available."
        )
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    public long RequiredBytes { get; }

    public long AvailableBytes { get; }
}
