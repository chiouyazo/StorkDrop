namespace StorkDrop.Contracts;

/// <summary>
/// Optional interface that plugins can implement to resolve template variables
/// in product install paths (e.g., {StepsPath}, {CustomRoot}, etc.).
/// StorkDrop calls this after file handler configuration, before copying files.
/// </summary>
public interface IInstallPathResolver
{
    /// <summary>
    /// Resolves template variables in the given install path.
    /// Return the resolved path, or null to indicate no resolution was needed.
    /// </summary>
    /// <param name="targetPath">The raw target path, possibly containing templates like {StepsPath}.</param>
    /// <param name="context">The plugin context with config values from the file handler dialog, or null.</param>
    /// <returns>The resolved path, or null if this plugin doesn't handle any templates in the path.</returns>
    string? ResolveInstallPath(string targetPath, PluginContext? context);
}
