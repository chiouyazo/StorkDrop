namespace StorkDrop.Contracts.Models;

/// <summary>
/// Represents the stages of the installation process.
/// </summary>
public enum InstallStage
{
    /// <summary>The product package is being downloaded.</summary>
    Downloading,

    /// <summary>The downloaded archive is being extracted.</summary>
    Extracting,

    /// <summary>Files are being copied to the target directory.</summary>
    Installing,

    /// <summary>Plugin pre/post-install hooks are running.</summary>
    RunningPlugins,

    /// <summary>The installation is being verified.</summary>
    Verifying,

    /// <summary>The product is being uninstalled.</summary>
    Uninstalling,
}
