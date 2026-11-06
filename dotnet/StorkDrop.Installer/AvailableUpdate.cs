using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

public sealed record AvailableUpdate(InstalledProduct Installed, ProductManifest Latest);
