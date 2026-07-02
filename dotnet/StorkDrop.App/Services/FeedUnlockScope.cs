namespace StorkDrop.App.Services;

/// <summary>
/// A per-operation authorization scope. Feed ids unlocked within a scope are not
/// prompted for again while the scope is alive, so a batch (e.g. "Update all") prompts
/// at most once per locked feed while single operations always create a fresh scope.
/// </summary>
public sealed class FeedUnlockScope
{
    internal HashSet<string> UnlockedBaseFeedIds { get; } = new(StringComparer.Ordinal);
}
