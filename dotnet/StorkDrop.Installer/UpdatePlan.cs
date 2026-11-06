using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

public sealed record UpdatePlan(UpdateDecision Decision, ProductManifest? Manifest, string? FeedId);
