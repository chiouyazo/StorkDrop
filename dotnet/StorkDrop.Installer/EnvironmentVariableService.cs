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

    public EnvironmentVariableService(ILogger<EnvironmentVariableService> logger)
    {
        _logger = logger;
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
                    Environment.SetEnvironmentVariable(decl.Name, resolvedValue, target);
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
                    string? currentValue = Environment.GetEnvironmentVariable(decl.Name, target);

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
                        applied.Add(
                            new AppliedEnvironmentVariable(
                                decl.Name,
                                "append",
                                resolvedValue,
                                decl.Separator,
                                decl.Target
                            )
                        );
                        continue;
                    }

                    string newValue = currentValue is null
                        ? resolvedValue
                        : currentValue + decl.Separator + resolvedValue;

                    Environment.SetEnvironmentVariable(decl.Name, newValue, target);
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
    public async Task RemoveAsync(List<AppliedEnvironmentVariable> appliedVars)
    {
        await EnvVarLock.WaitAsync();
        try
        {
            RemoveInternal(appliedVars);
        }
        finally
        {
            EnvVarLock.Release();
        }
    }

    private void RemoveInternal(List<AppliedEnvironmentVariable> appliedVars)
    {
        foreach (AppliedEnvironmentVariable applied in appliedVars)
        {
            try
            {
                EnvironmentVariableTarget target = ParseTarget(applied.Target);

                if (applied.Action.Equals("set", StringComparison.OrdinalIgnoreCase))
                {
                    Environment.SetEnvironmentVariable(applied.Name, null, target);
                    _logger.LogInformation(
                        "Deleted environment variable {Name} ({Target})",
                        applied.Name,
                        target
                    );
                }
                else if (applied.Action.Equals("append", StringComparison.OrdinalIgnoreCase))
                {
                    string? currentValue = Environment.GetEnvironmentVariable(applied.Name, target);
                    if (currentValue is null)
                        continue;

                    string newValue = RemoveEntry(
                        currentValue,
                        applied.AppliedValue,
                        applied.Separator
                    );
                    Environment.SetEnvironmentVariable(applied.Name, newValue, target);

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
            }
        }
    }

    public async Task SaveAppliedAsync(
        string productId,
        List<AppliedEnvironmentVariable> applied,
        CancellationToken cancellationToken
    )
    {
        if (applied.Count == 0)
            return;

        try
        {
            string configDir = GetStorkConfigDir();
            Directory.CreateDirectory(configDir);
            string path = Path.Combine(configDir, $"{productId}.envvars.json");
            string json = JsonSerializer.Serialize(applied, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save env var tracking for {ProductId}", productId);
        }
    }

    public async Task<List<AppliedEnvironmentVariable>> LoadAppliedAsync(
        string productId,
        CancellationToken cancellationToken
    )
    {
        string path = Path.Combine(GetStorkConfigDir(), $"{productId}.envvars.json");
        if (!File.Exists(path))
            return [];

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

    public void DeleteTracking(string productId)
    {
        try
        {
            string path = Path.Combine(GetStorkConfigDir(), $"{productId}.envvars.json");
            if (File.Exists(path))
                File.Delete(path);
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
