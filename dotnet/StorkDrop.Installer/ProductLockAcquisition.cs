namespace StorkDrop.Installer;

/// <summary>
/// Result of <see cref="ProductOperationLock.TryAcquire(string, string?, string)"/>. Disposing releases
/// the lock when this instance owns it; an acquisition that is not <see cref="Acquired"/>, re-entrant
/// or inherited from a parent process owns nothing and disposes as a no-op.
/// </summary>
public sealed class ProductLockAcquisition : IDisposable
{
    private readonly Action? _release;
    private bool _disposed;

    internal ProductLockAcquisition(bool acquired, ProductLockHolder? holder, Action? release)
    {
        Acquired = acquired;
        Holder = holder;
        _release = release;
    }

    public bool Acquired { get; }

    public ProductLockHolder? Holder { get; }

    public string DescribeConflict(string productTitle)
    {
        if (Holder is null)
            return $"Another operation on {productTitle} is already in progress.";

        return $"Another operation on {productTitle} is already in progress ({Holder.Operation} by {Holder.ProcessName}, PID {Holder.ProcessId}, since {Holder.Since.ToLocalTime():HH:mm:ss}). Wait for it to finish and try again.";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _release?.Invoke();
    }
}
