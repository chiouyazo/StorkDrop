namespace StorkDrop.Contracts;

/// <summary>
/// Represents a validation error for a specific configuration field.
/// </summary>
public sealed class PluginValidationError
{
    /// <summary>
    /// Gets or sets the key of the field that failed validation.
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginValidationError"/> class.
    /// </summary>
    public PluginValidationError() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginValidationError"/> class
    /// with the specified field key and error message.
    /// </summary>
    /// <param name="fieldKey">The key of the field that failed validation.</param>
    /// <param name="message">The human-readable error message.</param>
    public PluginValidationError(string fieldKey, string message)
    {
        FieldKey = fieldKey;
        Message = message;
    }
}
