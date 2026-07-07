namespace StorkDrop.Contracts.Services;

/// <summary>
/// Process-wide holder for the active <see cref="BrandingInfo"/>. Initialized once at startup from the
/// installation's whitelabel config, before any path or UI is resolved. Defaults to unbranded StorkDrop.
/// </summary>
public static class Branding
{
    /// <summary>Stable id of the feed a white-label edition pre-configures, so the UI can identify and lock it.</summary>
    public const string WhitelabelFeedId = "whitelabel-primary";

    public static BrandingInfo Current { get; private set; } = BrandingInfo.Default;

    public static void Initialize(BrandingInfo info) => Current = info;
}
