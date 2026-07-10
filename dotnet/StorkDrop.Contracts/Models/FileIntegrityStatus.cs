namespace StorkDrop.Contracts.Models;

/// <summary>Result of verifying one tracked file against its recorded install-time hash.</summary>
public enum FileIntegrityStatus
{
    /// <summary>On-disk file matches the recorded hash.</summary>
    Ok = 0,

    /// <summary>On-disk file differs from the recorded hash (corrupted or modified).</summary>
    Modified = 1,

    /// <summary>The file is tracked but no longer present on disk.</summary>
    Missing = 2,

    /// <summary>No hash was recorded (legacy install), so the file cannot be checked.</summary>
    Unverifiable = 3,
}
