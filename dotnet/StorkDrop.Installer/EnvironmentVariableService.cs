using System.Text.Json;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

/// <summary>
/// Applies and removes environment variable changes declared in product manifests.
/// </summary>
public sealed class EnvironmentVariableService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private static readonly SemaphoreSlim EnvVarLock = new SemaphoreSlim(1, 1);
    private readonly ILogger<EnvironmentVariableService> _logger;
    private readonly IEnvironmentVariableStore _store;

    public EnvironmentVariableService(ILogger<EnvironmentVariableService> logger)
        : this(logger, new SystemEnvironmentVariableStore()) { }

    internal EnvironmentVariableService(
        ILogger<EnvironmentVariableService> logger,
        IEnvironmentVariableStore store
    )
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>
    /// Applies all environment variable declarations from the manifest.
    /// </summary>
    public async Task<List<AppliedEnvironmentVariable>> ApplyAsync(
        EnvironmentVariableInfo[] declarations,
        string installPath
    )
    {
        await EnvVarLock.WaitAsync();
        try
        {
            return ApplyInternal(declarations, installPath);
        }
        finally
        {
            EnvVarLock.Release();
        }
    }

    /// <summary>
    /// Applies the declarations and persists the tracking file before the first variable is touched,
    /// so a variable that was set can always be removed again. The tracking is then narrowed to what
    /// was actually applied.
    /// </summary>
    public async Task<List<AppliedEnvironmentVariable>> ApplyTrackedAsync(
        string productId,
        string uniqueId,
        EnvironmentVariableInfo[] declarations,
        string installPath,
        CancellationToken cancellationToken
    )
    {
        await EnvVarLock.WaitAsync(cancellationToken);
        try
        {
            List<AppliedEnvironmentVariable> planned = declarations
                .Where(d =>
                    d.Action.Equals("set", StringComparison.OrdinalIgnoreCase)
                    || d.Action.Equals("append", StringComparison.OrdinalIgnoreCase)
                )
                .Select(d => new AppliedEnvironmentVariable(
                    d.Name,
                    d.Action.ToLowerInvariant(),
                    ResolveTemplates(d.Value, installPath),
                    d.Separator,
                    d.Target
                ))
                .ToList();
            await SaveAppliedAsync(productId, uniqueId, planned, cancellationToken);

            List<AppliedEnvironmentVariable> applied = ApplyInternal(declarations, installPath);
            if (applied.Count == 0)
                DeleteTracking(productId, uniqueId);
            else
                await SaveAppliedAsync(productId, uniqueId, applied, cancellationToken);
            return applied;
        }
        finally
        {
            EnvVarLock.Release();
        }
    }

    private List<AppliedEnvironmentVariable> ApplyInternal(
        EnvironmentVariableInfo[] declarations,
        string installPath
    )
    {
        List<AppliedEnvironmentVariable> applied = [];

        foreach (EnvironmentVariableInfo decl in declarations)
        {
            try
            {
                string resolvedValue = ResolveTemplates(decl.Value, installPath);
                EnvironmentVariableTarget target = ParseTarget(decl.Target);

                if (decl.Action.Equals("set", StringComparison.OrdinalIgnoreCase))
                {
                    _store.Set(decl.Name, resolvedValue, target);
                    _logger.LogInformation(
                        "Set environment variable {Name}={Value} ({Target})",
                        decl.Name,
                        resolvedValue,
                        target
                    );

                    applied.Add(
                        new AppliedEnvironmentVariable(
                            decl.Name,
                            "set",
                            resolvedValue,
                            decl.Separator,
                            decl.Target
                        )
                    );
                }
                else if (decl.Action.Equals("append", StringComparison.OrdinalIgnoreCase))
                {
                    string? currentValue = _store.Get(decl.Name, target);

                    if (currentValue is null && decl.MustExist)
                    {
                        _logger.LogInformation(
                            "Skipping append to {Name}: variable does not exist and mustExist=true",
                            decl.Name
                        );
                        continue;
                    }

                    if (
                        currentValue is not null
                        && ContainsEntry(currentValue, resolvedValue, decl.Separator)
                    )
                    {
                        _logger.LogInformation(
                            "Value already present in {Name}, skipping append",
                            decl.Name
                        );
                        continue;
                    }

                    string newValue = currentValue is null
                        ? resolvedValue
                        : currentValue + decl.Separator + resolvedValue;

                    _store.Set(decl.Name, newValue, target);
                    _logger.LogInformation(
                        "Appended to environment variable {Name}: {Value} ({Target})",
                        decl.Name,
                        resolvedValue,
                        target
                    );

                    applied.Add(
                        new AppliedEnvironmentVariable(
                            decl.Name,
                            "append",
                            resolvedValue,
                            decl.Separator,
                            decl.Target
                        )
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "Unknown environment variable action '{Action}' for {Name}",
                        decl.Action,
                        decl.Name
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply environment variable {Name}", decl.Name);
            }
        }

        return applied;
    }

    /// <summary>
    /// Removes all environment variable changes that were applied during installation.
    /// </summary>
    public async Task<List<AppliedEnvironmentVariable>> RemoveAsync(
        List<AppliedEnvironmentVariable> appliedVars
    )
    {
        await EnvVarLock.WaitAsync();
        try
        {
            return RemoveInternal(appliedVars);
        }
        finally
        {
            EnvVarLock.Release();
        }
    }

    /// <summary>
    /// Removes everything the tracking file lists and deletes the file only when all removals
    /// succeeded; otherwise the tracking is narrowed to the entries that are still applied.
    /// Returns the entries that could not be removed.
    /// </summary>
    public async Task<List<AppliedEnvironmentVariable>> RemoveTrackedAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        List<AppliedEnvironmentVariable> applied = await LoadAppliedAsync(
            productId,
            uniqueId,
            cancellationToken
        );
        List<AppliedEnvironmentVariable> failed =
            applied.Count > 0 ? await RemoveAsync(applied) : [];
        if (failed.Count == 0)
            DeleteTracking(productId, uniqueId);
        else
            await SaveAppliedAsync(productId, uniqueId, failed, cancellationToken);
        return failed;
    }

    private List<AppliedEnvironmentVariable> RemoveInternal(
        List<AppliedEnvironmentVariable> appliedVars
    )
    {
        List<AppliedEnvironmentVariable> failed = [];
        foreach (AppliedEnvironmentVariable applied in appliedVars)
        {
            try
            {
                EnvironmentVariableTarget target = ParseTarget(applied.Target);

                if (applied.Action.Equals("set", StringComparison.OrdinalIgnoreCase))
                {
                    _store.Set(applied.Name, null, target);
                    _logger.LogInformation(
                        "Deleted environment variable {Name} ({Target})",
                        applied.Name,
                        target
                    );
                }
                else if (applied.Action.Equals("append", StringComparison.OrdinalIgnoreCase))
                {
                    string? currentValue = _store.Get(applied.Name, target);
                    if (currentValue is null)
                        continue;

                    string newValue = RemoveEntry(
                        currentValue,
                        applied.AppliedValue,
                        applied.Separator
                    );
                    _store.Set(applied.Name, newValue, target);

                    _logger.LogInformation(
                        "Removed appended value from {Name}: {Value} ({Target})",
                        applied.Name,
                        applied.AppliedValue,
                        target
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to remove environment variable change for {Name}",
                    applied.Name
                );
                failed.Add(applied);
            }
        }

        return failed;
    }

    public async Task SaveAppliedAsync(
        string productId,
        string uniqueId,
        List<AppliedEnvironmentVariable> applied,
        CancellationToken cancellationToken
    )
    {
        if (applied.Count == 0)
            return;

        string path = StorkPaths.EnvVarsPath(productId, uniqueId);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(applied, JsonOptions);
        await SafeFileWriter.WriteAtomicAsync(path, json, cancellationToken);
    }

    public async Task<List<AppliedEnvironmentVariable>> LoadAppliedAsync(
        string productId,
        string uniqueId,
        CancellationToken cancellationToken
    )
    {
        string path = StorkPaths.EnvVarsPath(productId, uniqueId);
        if (!File.Exists(path))
        {
            string legacyPath = StorkPaths.LegacyEnvVarsPath(productId);
            if (File.Exists(legacyPath))
                path = legacyPath;
            else
                return [];
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<List<AppliedEnvironmentVariable>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void DeleteTracking(string productId, string uniqueId)
    {
        try
        {
            string path = StorkPaths.EnvVarsPath(productId, uniqueId);
            if (File.Exists(path))
                File.Delete(path);

            string legacyPath = StorkPaths.LegacyEnvVarsPath(productId);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
        catch
        {
            // Best effort
        }
    }

    internal static string ResolveTemplates(string value, string installPath) =>
        value.Replace("{InstallPath}", installPath, StringComparison.OrdinalIgnoreCase);

    internal static bool ContainsEntry(string current, string entry, string separator)
    {
        string[] parts = current.Split(separator, StringSplitOptions.None);
        return parts.Any(p => p.Equals(entry, StringComparison.OrdinalIgnoreCase));
    }

    internal static string RemoveEntry(string current, string entry, string separator)
    {
        string[] parts = current.Split(separator, StringSplitOptions.None);
        IEnumerable<string> filtered = parts.Where(p =>
            !p.Equals(entry, StringComparison.OrdinalIgnoreCase)
        );
        return string.Join(separator, filtered);
    }

    private static EnvironmentVariableTarget ParseTarget(string target) =>
        target.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableTarget.User
            : EnvironmentVariableTarget.Machine;

    private static string GetStorkConfigDir() => StorkPaths.ConfigDir;
}
