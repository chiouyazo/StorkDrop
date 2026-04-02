namespace StorkDrop.App.Services;

public sealed record PostProductResolution(
    IReadOnlyList<ResolvedPostProduct> Available,
    IReadOnlyList<ResolvedPostProduct> AlreadyInstalled,
    IReadOnlyList<string> Warnings
);
