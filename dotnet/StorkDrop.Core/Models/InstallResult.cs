namespace StorkDrop.Core.Models;

/// <summary>
/// Represents the result of an installation operation, including structured error information.
/// </summary>
public sealed class InstallResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the installation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if the installation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the name of the step that failed, if applicable.
    /// </summary>
    public string? FailedStep { get; set; }

    /// <summary>
    /// Gets or sets the exception that caused the failure, if applicable.
    /// </summary>
    public Exception? Exception { get; set; }
}
