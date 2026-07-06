namespace StorkDrop.Contracts;

/// <summary>
/// Severity of a <see cref="PluginPreInstallResult"/>, controlling how the installation engine
/// reacts to it.
/// </summary>
public enum PluginResultSeverity
{
    /// <summary>No problem.</summary>
    Ok = 0,

    /// <summary>
    /// Advisory problem.
    /// </summary>
    Warning = 1,

    /// <summary>Hard failur</summary>
    Blocking = 2,
}
