namespace StorkDrop.Registry.S3;

/// <summary>
/// S3 key layout, mirroring the Nexus raw-repository structure with the channel as the top-level
/// prefix (so a channel is the access-scoping boundary, granted per IAM prefix). There are no extra
/// index/catalog/pointer objects: discovery is done by listing, exactly like the Nexus backend does
/// via the components API. All keys are relative to an optional base <c>prefix</c>.
///
/// <code>
/// {prefix}{channel}/{productId}/manifest.json                                  latest (copy of newest version)
/// {prefix}{channel}/{productId}/versions/{version}/manifest.json
/// {prefix}{channel}/{productId}/versions/{version}/{productId}-{version}.zip
/// </code>
/// </summary>
public sealed class S3Layout
{
    private readonly string _prefix;

    public S3Layout(string? prefix)
    {
        _prefix = Normalize(prefix);
    }

    private static string Normalize(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return string.Empty;
        string trimmed = prefix.Trim().Trim('/');
        return trimmed.Length == 0 ? string.Empty : trimmed + "/";
    }

    /// <summary>Root of a channel; listing this with a "/" delimiter yields the product ids.</summary>
    public string ChannelRoot(string channel) => $"{_prefix}{channel}/";

    public string ProductRoot(string channel, string productId) =>
        $"{_prefix}{channel}/{productId}/";

    /// <summary>The latest-version manifest copy (same convention as the Nexus root manifest).</summary>
    public string LatestManifestKey(string channel, string productId) =>
        ProductRoot(channel, productId) + "manifest.json";

    /// <summary>Root of a product's versions; listing this with a "/" delimiter yields the versions.</summary>
    public string VersionsRoot(string channel, string productId) =>
        ProductRoot(channel, productId) + "versions/";

    public string VersionManifestKey(string channel, string productId, string version) =>
        VersionsRoot(channel, productId) + $"{version}/manifest.json";

    public string PackageKey(string channel, string productId, string version) =>
        VersionsRoot(channel, productId) + $"{version}/{productId}-{version}.zip";
}
