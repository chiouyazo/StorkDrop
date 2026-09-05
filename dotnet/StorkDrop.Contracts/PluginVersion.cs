using StorkDrop.Contracts.Services;

namespace StorkDrop.Contracts;

/// <summary>
/// A comparable product/plugin version. Wraps a raw version string and orders it with the same
/// SemVer-style rules as <see cref="VersionComparer"/> (pre-release and build-metadata aware), which
/// <see cref="System.Version"/> cannot parse. Lets a plugin express "applies from version X" without
/// hand-rolling comparer calls, e.g.
/// <c>PluginVersion.Parse(ctx.Version) &gt;= PluginVersion.Parse("2.5.0")</c>.
/// </summary>
public readonly struct PluginVersion : IComparable<PluginVersion>, IEquatable<PluginVersion>
{
    private readonly string? _value;

    /// <summary>The raw version string this was created from.</summary>
    public string Value => _value ?? string.Empty;

    private PluginVersion(string value) => _value = value;

    /// <summary>
    /// Parses a version string, throwing <see cref="FormatException"/> when it is not a valid version
    /// (see <see cref="VersionComparer.IsValid"/>).
    /// </summary>
    public static PluginVersion Parse(string version)
    {
        if (!TryParse(version, out PluginVersion result))
            throw new FormatException($"'{version}' is not a valid version.");
        return result;
    }

    /// <summary>Tries to parse a version string; returns false when it is not a valid version.</summary>
    public static bool TryParse(string? version, out PluginVersion result)
    {
        if (version is not null && VersionComparer.IsValid(version))
        {
            result = new PluginVersion(version);
            return true;
        }
        result = default;
        return false;
    }

    public int CompareTo(PluginVersion other) =>
        VersionComparer.Instance.Compare(Value, other.Value);

    public bool Equals(PluginVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is PluginVersion other && Equals(other);

    public override int GetHashCode() => CanonicalKey(Value).GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public static bool operator ==(PluginVersion left, PluginVersion right) => left.Equals(right);

    public static bool operator !=(PluginVersion left, PluginVersion right) => !left.Equals(right);

    public static bool operator <(PluginVersion left, PluginVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(PluginVersion left, PluginVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(PluginVersion left, PluginVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(PluginVersion left, PluginVersion right) =>
        left.CompareTo(right) >= 0;

    // Canonical form so versions that VersionComparer treats as equal (1.0 == 1.0.0, pre-release
    // 01 == 1) also hash equal. Mirrors the comparer's normalisation: drop leading v and +build,
    // imply trailing-zero numeric parts, strip leading zeros from numeric pre-release identifiers.
    private static string CanonicalKey(string version)
    {
        string core = version;
        if (core.Length > 0 && (core[0] == 'v' || core[0] == 'V'))
            core = core[1..];

        int plus = core.IndexOf('+');
        if (plus >= 0)
            core = core[..plus];

        string pre = string.Empty;
        int dash = core.IndexOf('-');
        if (dash >= 0)
        {
            pre = core[(dash + 1)..];
            core = core[..dash];
        }

        List<long> numbers = [];
        foreach (string segment in core.Split('.'))
            numbers.Add(long.TryParse(segment, out long n) ? n : 0);
        while (numbers.Count > 1 && numbers[^1] == 0)
            numbers.RemoveAt(numbers.Count - 1);
        string numericKey = string.Join('.', numbers);

        if (pre.Length == 0)
            return numericKey;

        string[] identifiers = pre.Split('.');
        for (int i = 0; i < identifiers.Length; i++)
        {
            if (IsAllDigits(identifiers[i]))
            {
                string trimmed = identifiers[i].TrimStart('0');
                identifiers[i] = trimmed.Length > 0 ? trimmed : "0";
            }
        }
        return $"{numericKey}-{string.Join('.', identifiers)}";
    }

    private static bool IsAllDigits(string value)
    {
        if (value.Length == 0)
            return false;
        foreach (char c in value)
        {
            if (!char.IsDigit(c))
                return false;
        }
        return true;
    }
}
