namespace StorkDrop.Contracts.Services;

/// <summary>
/// Outcome of spawning an elevated child process: it either ran to success, was refused before any
/// elevated process started (user cancelled the UAC request, or elevation is blocked), or it started
/// elevated but the operation failed inside it. The last case must not be reported as a refusal.
/// </summary>
public enum ElevationResult
{
    Succeeded,
    DeniedByUser,
    Failed,
}
