namespace StorkDrop.Contracts;

/// <summary>
/// Describes one field in the dynamic pre-install configuration UI.
/// </summary>
public sealed class PluginConfigField
{
    /// <summary>
    /// Gets or sets the unique key identifying this configuration field.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display label shown to the user.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional description or help text for this field.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the type of input control to render for this field.
    /// </summary>
    public PluginFieldType FieldType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this field must be filled in.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the default value to pre-populate in the UI.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// For <see cref="PluginFieldType.Dropdown"/> and <see cref="PluginFieldType.MultiSelect"/>:
    /// the available options. Can be static or built from <see cref="PluginEnvironment"/>.
    /// </summary>
    public List<PluginOptionItem> Options { get; set; } = new List<PluginOptionItem>();

    /// <summary>
    /// For <see cref="PluginFieldType.Number"/>: minimum value.
    /// </summary>
    public double? Min { get; set; }

    /// <summary>
    /// For <see cref="PluginFieldType.Number"/>: maximum value.
    /// </summary>
    public double? Max { get; set; }

    /// <summary>
    /// For <see cref="PluginFieldType.Group"/>: the fields that make up each group instance.
    /// Each instance gets its own copy of these sub-fields.
    /// </summary>
    public List<PluginConfigField> SubFields { get; set; } = new List<PluginConfigField>();

    /// <summary>
    /// Whether this field is enabled and interactive. When false, the field is rendered
    /// but greyed out. Used as the initial state if <see cref="EnabledWhen"/> is not set.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this field is read-only. When true, the field displays its value but
    /// cannot be edited by the user.
    /// </summary>
    public bool IsReadOnly { get; set; } = false;

    /// <summary>
    /// Optional condition that dynamically controls whether this field is enabled.
    /// Receives the current values of all fields and returns true if the field should be enabled.
    /// Evaluated whenever any field value changes in the dialog.
    /// </summary>
    public Func<Dictionary<string, string>, bool>? EnabledWhen { get; set; }
}
