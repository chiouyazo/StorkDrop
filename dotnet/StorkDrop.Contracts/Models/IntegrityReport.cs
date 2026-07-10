using System.Linq;

namespace StorkDrop.Contracts.Models;

/// <summary>
/// Outcome of verifying a product instance's tracked files against their recorded install-time
/// hashes. Only files StorkDrop installed from the product's content archive are checked.
/// </summary>
public sealed record IntegrityReport(
    string ProductId,
    string InstanceUniqueId,
    IReadOnlyList<FileIntegrityEntry> Files
)
{
    /// <summary>Files that are corrupted/modified or missing, i.e. the ones a repair would fix.</summary>
    public IReadOnlyList<FileIntegrityEntry> Problems =>
        Files
            .Where(f => f.Status is FileIntegrityStatus.Modified or FileIntegrityStatus.Missing)
            .ToList();

    public bool HasProblems => Problems.Count > 0;

    public int OkCount => Files.Count(f => f.Status == FileIntegrityStatus.Ok);

    public int UnverifiableCount => Files.Count(f => f.Status == FileIntegrityStatus.Unverifiable);
}
