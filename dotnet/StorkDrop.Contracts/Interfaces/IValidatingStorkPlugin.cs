namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Optional interface for plugins that support configuration validation.
/// Implement this alongside <see cref="IStorkPlugin"/> for inline field validation.
/// Backward compatible: plugins that do not implement this are not validated.
/// </summary>
public interface IValidatingStorkPlugin : IStorkPlugin
{
    /// <summary>
    /// Validates the user's configuration choices before install begins.
    /// Return an empty list if all values are valid.
    /// </summary>
    /// <param name="context">The full plugin context containing user configuration values.</param>
    /// <returns>A list of validation errors, or an empty list if validation passes.</returns>
    IReadOnlyList<PluginValidationError> ValidateConfiguration(PluginContext context);
}

/// <summary>
/// Extension methods for <see cref="IStorkPlugin"/> to support validation
/// in a backward-compatible manner.
/// </summary>
public static class StorkPluginExtensions
{
    /// <summary>
    /// Validates configuration if the plugin implements <see cref="IValidatingStorkPlugin"/>,
    /// otherwise returns an empty list.
    /// </summary>
    /// <param name="plugin">The plugin instance to validate against.</param>
    /// <param name="context">The full plugin context containing user configuration values.</param>
    /// <returns>A list of validation errors, or an empty list if validation passes or is not supported.</returns>
    public static IReadOnlyList<PluginValidationError> TryValidateConfiguration(
        this IStorkPlugin plugin,
        PluginContext context
    )
    {
        if (plugin is IValidatingStorkPlugin validatingPlugin)
            return validatingPlugin.ValidateConfiguration(context);
        return new List<PluginValidationError>();
    }
}
