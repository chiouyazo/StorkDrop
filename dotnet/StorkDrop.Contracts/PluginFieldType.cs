namespace StorkDrop.Contracts;

/// <summary>
/// Defines the types of input controls available for plugin configuration fields.
/// </summary>
public enum PluginFieldType
{
    /// <summary>A single-line text input.</summary>
    Text,

    /// <summary>A numeric input.</summary>
    Number,

    /// <summary>A dropdown selection with a single choice.</summary>
    Dropdown,

    /// <summary>A multi-select list allowing multiple choices.</summary>
    MultiSelect,

    /// <summary>A boolean checkbox.</summary>
    Checkbox,

    /// <summary>A password input with masked text.</summary>
    Password,

    /// <summary>A file path browser.</summary>
    FilePath,

    /// <summary>A folder path browser.</summary>
    FolderPath,

    /// <summary>
    /// A repeatable group of sub-fields. Users can add/remove instances.
    /// The sub-fields are defined in PluginConfigField.SubFields.
    /// Values are stored as JSON array in the field's Value.
    /// </summary>
    Group,
}
