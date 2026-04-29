using System.Text.RegularExpressions;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Provides validation and utilities for product instance identifiers.
/// </summary>
public static class InstanceIdHelper
{
    /// <summary>
    /// The default instance identifier used for single-instance products.
    /// </summary>
    public const string DefaultInstanceId = "default";

    private static readonly Regex ValidPattern = new Regex(
        @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Determines whether the specified instance identifier is valid.
    /// Valid identifiers start with an alphanumeric character and may contain
    /// alphanumeric characters, hyphens, and underscores (max 64 characters).
    /// </summary>
    /// <param name="instanceId">The instance identifier to validate.</param>
    /// <returns><c>true</c> if the identifier is valid; otherwise, <c>false</c>.</returns>
    public static bool IsValid(string instanceId)
    {
        return ValidPattern.IsMatch(instanceId);
    }

    /// <summary>
    /// Sanitizes an input string into a valid instance identifier.
    /// Converts to lowercase, replaces spaces with hyphens, and strips invalid characters.
    /// </summary>
    /// <param name="input">The raw input string to sanitize.</param>
    /// <returns>A sanitized instance identifier.</returns>
    public static string Sanitize(string input)
    {
        string sanitized = input.Trim().ToLowerInvariant().Replace(' ', '-');
        sanitized = Regex.Replace(sanitized, @"[^a-z0-9_-]", "");

        if (sanitized.Length == 0)
            return DefaultInstanceId;

        if (!char.IsLetterOrDigit(sanitized[0]))
            sanitized = "i" + sanitized;

        if (sanitized.Length > 64)
            sanitized = sanitized[..64];

        return sanitized;
    }
}
