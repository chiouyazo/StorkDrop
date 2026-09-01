namespace StorkDrop.Contracts.Models;

/// <summary>
/// An installed product that depends on a just-updated product (via <c>requiredProductIds</c>) and has
/// a newer version available on its own installed channel.
/// </summary>
public sealed record DependentUpdate(InstalledProduct Installed, ProductManifest TargetManifest)
{
    public string Title => Installed.Title;
    public string CurrentVersion => Installed.Version;
    public string TargetVersion => TargetManifest.Version;
}
