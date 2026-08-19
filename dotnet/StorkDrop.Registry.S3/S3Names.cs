using System.Linq;

namespace StorkDrop.Registry.S3;

/// <summary>
/// Validates the free-form strings (channel, productId, version) that become S3 key path segments.
/// Because access is scoped by key prefix, an unvalidated segment could break listing (a "/" splits a
/// name across folders), corrupt the composite feed id (":"), widen an IAM policy (a literal "*"), or
/// escape a prefix ("..") on gateways that normalize paths. Segments are therefore restricted to safe
/// single path components and validated at every write boundary.
/// </summary>
public static class S3Names
{
    private static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public static bool IsValidSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name != name.Trim())
            return false;
        if (name == "." || name.Contains(".."))
            return false;
        if (name.IndexOfAny(Forbidden) >= 0)
            return false;
        return !name.Any(char.IsControl);
    }

    public static string Require(string? name, string kind)
    {
        if (!IsValidSegment(name))
        {
            throw new ArgumentException(
                $"Invalid {kind} '{name}': must be a single S3 path segment without "
                    + "/ \\ : * ? \" < > | , '..', control characters, or surrounding whitespace.",
                kind
            );
        }
        return name!;
    }
}
