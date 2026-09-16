using StorkDrop.Contracts;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Installer;

/// <summary>
/// Applies install-path token resolvers in order, each one optionally rewriting the path (a null return
/// means "nothing to resolve here"). Used so the installing product's own
/// <see cref="IInstallPathResolver"/> plugins resolve the tokens in their own install path with their
/// own config, before the host-wide resolver chain gets a fallback turn.
/// </summary>
public static class InstallPathResolution
{
    public static string Apply(
        string targetPath,
        IEnumerable<IInstallPathResolver> resolvers,
        PluginContext? context
    )
    {
        string current = targetPath;
        foreach (IInstallPathResolver resolver in resolvers)
        {
            string? resolved = resolver.ResolveInstallPath(current, context);
            if (resolved is not null)
                current = resolved;
        }
        return current;
    }
}
