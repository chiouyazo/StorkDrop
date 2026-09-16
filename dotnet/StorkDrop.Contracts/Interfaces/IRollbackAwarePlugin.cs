namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Optional plugin capability: StorkDrop calls <see cref="PrepareForRollbackAsync"/> before it restores
/// a backup after a failed update, so the plugin can release file locks that would otherwise make the
/// restore fail - typically by stopping its own Windows services. Best-effort: a failure here is logged
/// and the rollback continues.
/// </summary>
public interface IRollbackAwarePlugin
{
    Task PrepareForRollbackAsync(
        PluginContext context,
        CancellationToken cancellationToken = default
    );
}
