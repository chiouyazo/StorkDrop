using System.Text.RegularExpressions;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// A recommended-install-path token that anchors a product's files inside an already-installed
/// product's instance directory, e.g. <c>{ProductPath:acme.suite}/addons/plugins</c>.
/// StorkDrop resolves it by asking which installed instance of the referenced product to target and
/// substituting that instance's install path.
/// </summary>
public static class ProductPathToken
{
    private static readonly Regex TokenRegex = new(
        @"\{ProductPath:(?<id>[^}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Returns the referenced product id if <paramref name="path"/> contains a product-path token,
    /// otherwise null.
    /// </summary>
    public static string? GetReferencedProductId(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        Match match = TokenRegex.Match(path);
        if (!match.Success)
            return null;

        string id = match.Groups["id"].Value.Trim();
        return id.Length == 0 ? null : id;
    }

    /// <summary>
    /// Replaces the product-path token in <paramref name="path"/> with
    /// <paramref name="instanceInstallPath"/> (trailing separators trimmed so the join is clean).
    /// </summary>
    public static string Resolve(string path, string instanceInstallPath)
    {
        string root = instanceInstallPath.TrimEnd('/', '\\');
        return TokenRegex.Replace(path, root.Replace("$", "$$"));
    }
}
