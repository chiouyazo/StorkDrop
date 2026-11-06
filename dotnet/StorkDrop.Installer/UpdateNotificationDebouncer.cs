using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

/// <summary>
/// Remembers which update was already announced per installed instance, so the periodic check does
/// not repeat the same notification every interval.
/// </summary>
public sealed class UpdateNotificationDebouncer
{
    private readonly Dictionary<string, string> _notified = new Dictionary<string, string>(
        StringComparer.Ordinal
    );

    public bool ShouldNotify(AvailableUpdate update)
    {
        string key = Key(update.Installed);
        if (
            _notified.TryGetValue(key, out string? lastVersion)
            && string.Equals(lastVersion, update.Latest.Version, StringComparison.Ordinal)
        )
            return false;

        _notified[key] = update.Latest.Version;
        return true;
    }

    /// <summary>
    /// Forgets instances that were checked successfully and no longer have an update, so a later
    /// update is announced again. Instances that could not be checked keep their state.
    /// </summary>
    public void Forget(
        IEnumerable<InstalledProduct> checkedInstances,
        IEnumerable<AvailableUpdate> currentUpdates
    )
    {
        HashSet<string> withUpdate = currentUpdates
            .Select(u => Key(u.Installed))
            .ToHashSet(StringComparer.Ordinal);
        foreach (InstalledProduct instance in checkedInstances)
        {
            string key = Key(instance);
            if (!withUpdate.Contains(key))
                _notified.Remove(key);
        }
    }

    private static string Key(InstalledProduct product) =>
        $"{product.ProductId}|{product.InstanceId}";
}
