namespace StorkDrop.App.Services;

/// <summary>
/// Gates mutating operations (install/update/uninstall/action) behind the optional
/// per-feed soft-lock password. Non-locked feeds pass through transparently.
/// </summary>
public interface IFeedLockService
{
    /// <summary>Whether the feed identified by <paramref name="feedId"/> is locked.</summary>
    Task<bool> IsLockedAsync(string? feedId);

    /// <summary>
    /// Ensures the caller is authorized to operate on <paramref name="feedId"/>. Returns
    /// true immediately for unlocked feeds; otherwise prompts for the feed password and
    /// returns true only once it is entered correctly. Returns false if the user cancels.
    /// </summary>
    Task<bool> EnsureAuthorizedAsync(
        string? feedId,
        string operationName,
        FeedUnlockScope? scope = null
    );

    /// <summary>Creates a scope for a batch of operations (see <see cref="FeedUnlockScope"/>).</summary>
    FeedUnlockScope CreateScope();
}
