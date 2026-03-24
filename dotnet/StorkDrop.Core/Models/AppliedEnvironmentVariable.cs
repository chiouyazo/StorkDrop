namespace StorkDrop.Core.Models;

/// <summary>
/// Records an environment variable change applied during installation for precise rollback.
/// </summary>
/// <param name="Name">The environment variable name.</param>
/// <param name="Action">The action performed: "set" or "append".</param>
/// <param name="AppliedValue">The resolved value that was set or appended.</param>
/// <param name="Separator">The separator used for append operations.</param>
/// <param name="Target">The target scope: "machine" or "user".</param>
public sealed record AppliedEnvironmentVariable(
    string Name,
    string Action,
    string AppliedValue,
    string Separator = ";",
    string Target = "machine"
);
