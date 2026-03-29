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
    /// Gets or sets a list of validation errors that prevented the operation from proceeding.
    /// </summary>
    public IReadOnlyList<PluginValidationError> ValidationErrors { get; set; } =
        Array.Empty<PluginValidationError>();
}
