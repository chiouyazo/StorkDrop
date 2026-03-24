namespace StorkDrop.Contracts.Services;

public sealed class VersionComparer : IComparer<string>
{
    public static VersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        ReadOnlySpan<char> spanX = x.AsSpan();
        ReadOnlySpan<char> spanY = y.AsSpan();

        // Strip leading 'v' or 'V'
        if (spanX.Length > 0 && (spanX[0] == 'v' || spanX[0] == 'V'))
            spanX = spanX[1..];
        if (spanY.Length > 0 && (spanY[0] == 'v' || spanY[0] == 'V'))
            spanY = spanY[1..];

        // Split off pre-release suffix (everything after '-')
        ReadOnlySpan<char> preReleaseX = ReadOnlySpan<char>.Empty;
        ReadOnlySpan<char> preReleaseY = ReadOnlySpan<char>.Empty;

        int dashIndexX = spanX.IndexOf('-');
        if (dashIndexX >= 0)
        {
            preReleaseX = spanX[(dashIndexX + 1)..];
            spanX = spanX[..dashIndexX];
        }

        int dashIndexY = spanY.IndexOf('-');
        if (dashIndexY >= 0)
        {
            preReleaseY = spanY[(dashIndexY + 1)..];
            spanY = spanY[..dashIndexY];
        }

        // Compare numeric parts
        int result = CompareNumericParts(spanX, spanY);
        if (result != 0)
            return result;

        // Pre-release: a version without pre-release has higher precedence
        bool xHasPreRelease = preReleaseX.Length > 0;
        bool yHasPreRelease = preReleaseY.Length > 0;

        if (!xHasPreRelease && !yHasPreRelease)
            return 0;
        if (!xHasPreRelease && yHasPreRelease)
            return 1;
        if (xHasPreRelease && !yHasPreRelease)
            return -1;

        return preReleaseX.SequenceCompareTo(preReleaseY);
    }

    private static int CompareNumericParts(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        Span<int> partsX = stackalloc int[4];
        Span<int> partsY = stackalloc int[4];

        int countX = ParseParts(x, partsX);
        int countY = ParseParts(y, partsY);

        int maxParts = Math.Max(countX, countY);
        for (int i = 0; i < maxParts; i++)
        {
            int partX = i < countX ? partsX[i] : 0;
            int partY = i < countY ? partsY[i] : 0;

            if (partX < partY)
                return -1;
            if (partX > partY)
                return 1;
        }

        return 0;
    }

    private static int ParseParts(ReadOnlySpan<char> version, Span<int> parts)
    {
        int partIndex = 0;
        int current = 0;
        bool hasDigit = false;

        for (int i = 0; i < version.Length && partIndex < parts.Length; i++)
        {
            if (version[i] == '.')
            {
                if (hasDigit)
                {
                    parts[partIndex++] = current;
                    current = 0;
                    hasDigit = false;
                }
            }
            else if (char.IsDigit(version[i]))
            {
                current = current * 10 + (version[i] - '0');
                hasDigit = true;
            }
        }

        if (hasDigit && partIndex < parts.Length)
        {
            parts[partIndex++] = current;
        }

        return partIndex;
    }

    public static bool IsNewer(string candidate, string baseline) =>
        Instance.Compare(candidate, baseline) > 0;

    public static bool IsValid(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        ReadOnlySpan<char> span = version.AsSpan();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
            span = span[1..];

        int dashIndex = span.IndexOf('-');
        if (dashIndex >= 0)
            span = span[..dashIndex];

        if (span.Length == 0)
            return false;

        int dotCount = 0;
        bool hasDigitInSegment = false;

        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '.')
            {
                if (!hasDigitInSegment)
                    return false;
                dotCount++;
                hasDigitInSegment = false;
            }
            else if (char.IsDigit(span[i]))
            {
                hasDigitInSegment = true;
            }
            else
            {
                return false;
            }
        }

        return hasDigitInSegment && dotCount >= 1;
    }
}
