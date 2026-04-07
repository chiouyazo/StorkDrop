namespace StorkDrop.Contracts.Models;

/// <summary>
/// The type of product, affecting how it is displayed and managed in StorkDrop.
/// </summary>
public enum InstallType
{
    /// <summary>A plugin or module that extends another product.</summary>
    Plugin,

    /// <summary>A full application suite.</summary>
    Suite,

    /// <summary>A bundle of multiple products.</summary>
    Bundle,

    /// <summary>An action-only product that runs plugin steps but does not install persistent files.</summary>
    Executable,
}
