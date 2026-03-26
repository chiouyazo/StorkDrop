using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StorkDrop.Contracts;

/// <summary>
/// Declares that a plugin handles specific file types from product packages.
/// Files matching the registered extensions are NOT copied to the install directory
/// by StorkDrop. Instead, the plugin receives them via <see cref="HandleFilesAsync"/>
/// and decides what to do (e.g. deploy to a database, register with a service, etc.).
/// </summary>
public interface IFileTypeHandler
{
    /// <summary>
    /// File extensions this handler claims (e.g. ".sql", ".config").
    /// StorkDrop will not deploy these files - the plugin handles them entirely.
    /// </summary>
    IReadOnlyList<string> HandledExtensions { get; }

    /// <summary>
    /// Called after extraction with the list of matching files. Returns config fields
    /// that StorkDrop shows to the user before calling <see cref="HandleFilesAsync"/>.
    /// Use this to let the user choose which files to deploy, which database to target, etc.
    /// Return an empty list if no user input is needed.
    /// </summary>
    /// <param name="files">Full paths to the matching files found in the package.</param>
    /// <param name="context">Install context with config values and paths.</param>
    /// <returns>Config fields to show the user, or empty if no input needed.</returns>
    IReadOnlyList<PluginConfigField> GetFileHandlerConfig(
        IReadOnlyList<string> files,
        PluginContext context
    );

    /// <summary>
    /// Called with all files matching <see cref="HandledExtensions"/> found in the package.
    /// The plugin can install them to databases, run them as scripts, register them, etc.
    /// </summary>
    /// <param name="files">Full paths to the extracted files (in a temp directory).</param>
    /// <param name="context">Install context with database connections, paths, config values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure per file.</returns>
    Task<FileHandlerResult> HandleFilesAsync(
        IReadOnlyList<string> files,
        PluginContext context,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Result of handling custom file types during installation.
/// </summary>
public sealed class FileHandlerResult
{
    /// <summary>Whether all files were handled successfully.</summary>
    public bool Success { get; set; } = true;

    /// <summary>Per-file results for detailed reporting.</summary>
    public IReadOnlyList<FileHandlerFileResult> FileResults { get; set; } =
        System.Array.Empty<FileHandlerFileResult>();

    /// <summary>Overall error message if <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result for a single handled file.
/// </summary>
public sealed class FileHandlerFileResult
{
    /// <summary>The file that was processed.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Whether this file was handled successfully.</summary>
    public bool Success { get; set; } = true;

    /// <summary>What the handler did with this file (for logging/display).</summary>
    public string? Action { get; set; }

    /// <summary>Error message if this file failed.</summary>
    public string? ErrorMessage { get; set; }
}
