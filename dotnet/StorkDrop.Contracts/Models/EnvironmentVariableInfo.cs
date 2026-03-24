namespace StorkDrop.Contracts.Models;

/// <summary>
/// Describes an environment variable to set or modify during product installation.
/// </summary>
/// <param name="Name">The environment variable name (e.g., "ACME_HOME", "PATH").</param>
/// <param name="Value">The value or value fragment. Supports {InstallPath} substitution.</param>
/// <param name="Action">"set" to create/overwrite, "append" to append to an existing variable.</param>
/// <param name="MustExist">For "append" only. If true, skip when the variable does not exist.</param>
/// <param name="Separator">Delimiter for append (default ";").</param>
/// <param name="Target">"machine" (default) or "user" scope.</param>
public sealed record EnvironmentVariableInfo(
    string Name,
    string Value,
    string Action = "set",
    bool MustExist = false,
    string Separator = ";",
    string Target = "machine"
);
