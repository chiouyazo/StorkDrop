using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Contracts;

/// <summary>
/// Result returned by <see cref="IStorkPlugin.PreInstallAsync"/> and
/// <see cref="IStorkPlugin.PreUninstallAsync"/> to indicate success or failure
/// with structured error information.
/// </summary>
public sealed class PluginPreInstallResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the pre-install or pre-uninstall phase succeeded.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional message describing the outcome.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the severity of this result. When set to <see cref="PluginResultSeverity.Warning"/>,
    /// the engine prompts the user to proceed or cancel instead of aborting outright. Left at the
    /// default (<see cref="PluginResultSeverity.Ok"/>), a <see cref="Success"/> of <c>false</c> is
    /// treated as a blocking failure — matching the original behaviour.
    /// </summary>
    public PluginResultSeverity Severity { get; set; } = PluginResultSeverity.Ok;

    /// <summary>
    /// Gets or sets a list of validation errors that prevented the operation from proceeding.
    /// </summary>
    public IReadOnlyList<PluginValidationError> ValidationErrors { get; set; } =
        Array.Empty<PluginValidationError>();
}
