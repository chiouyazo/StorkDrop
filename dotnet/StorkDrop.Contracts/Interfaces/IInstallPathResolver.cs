namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Optional interface that plugins can implement to resolve template variables
/// in product install paths (e.g., {ACMEPath}, {CustomRoot}, etc.).
/// StorkDrop calls this after file handler configuration, before copying files.
/// </summary>
public interface IInstallPathResolver
{
    /// <summary>
    /// Resolves template variables in the given install path.
    /// Return the resolved path, or null to indicate no resolution was needed.
    /// </summary>
    /// <param name="targetPath">The raw target path, possibly containing templates like {ACMEPath}.</param>
    /// <param name="context">
    /// For the installing product's own plugin, the product's context with its config values (so it
    /// resolves tokens from its own selection). When invoked as a host-wide fallback it may instead be
    /// the file-handler context, or null.
    /// </param>
    /// <returns>The resolved path, or null if this plugin doesn't handle any templates in the path.</returns>
    string? ResolveInstallPath(string targetPath, PluginContext? context);
}
