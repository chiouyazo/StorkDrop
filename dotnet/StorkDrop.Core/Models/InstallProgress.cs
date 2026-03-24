namespace StorkDrop.Core.Models;

/// <summary>
/// Reports progress during an installation operation.
/// </summary>
public sealed record InstallProgress(InstallStage Stage, int Percentage, string Message);
