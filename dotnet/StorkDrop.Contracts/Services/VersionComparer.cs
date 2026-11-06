namespace StorkDrop.Contracts.Services;

public sealed class VersionComparer : IComparer<string>
{
    public static VersionComparer Instance { get; } = new VersionComparer();

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

        // Build metadata (after '+') is ignored for precedence per SemVer.
        int plusIndexX = spanX.IndexOf('+');
        if (plusIndexX >= 0)
            spanX = spanX[..plusIndexX];
        int plusIndexY = spanY.IndexOf('+');
        if (plusIndexY >= 0)
            spanY = spanY[..plusIndexY];

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

        return ComparePreRelease(preReleaseX, preReleaseY);
    }

    private static int ComparePreRelease(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        while (!x.IsEmpty || !y.IsEmpty)
        {
            if (x.IsEmpty)
                return -1;
            if (y.IsEmpty)
                return 1;

            ReadOnlySpan<char> idX = TakeIdentifier(ref x);
            ReadOnlySpan<char> idY = TakeIdentifier(ref y);

            bool numericX = IsAllDigits(idX);
            bool numericY = IsAllDigits(idY);

            int cmp;
            if (numericX && numericY)
                cmp = CompareNumericIdentifier(idX, idY);
            else if (numericX)
                cmp = -1;
            else if (numericY)
                cmp = 1;
            else
                cmp = CompareHyphenatedIdentifier(idX, idY);

            if (cmp != 0)
                return cmp;
        }

        return 0;
    }

    private static int CompareHyphenatedIdentifier(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        while (!x.IsEmpty || !y.IsEmpty)
        {
            if (x.IsEmpty)
                return -1;
            if (y.IsEmpty)
                return 1;

            ReadOnlySpan<char> segmentX = TakeSegment(ref x);
            ReadOnlySpan<char> segmentY = TakeSegment(ref y);

            bool numericX = IsAllDigits(segmentX);
            bool numericY = IsAllDigits(segmentY);

            int cmp;
            if (numericX && numericY)
                cmp = CompareNumericIdentifier(segmentX, segmentY);
            else if (numericX)
                cmp = -1;
            else if (numericY)
                cmp = 1;
            else
                cmp = segmentX.SequenceCompareTo(segmentY);

            if (cmp != 0)
                return cmp;
        }

        return 0;
    }

    private static ReadOnlySpan<char> TakeSegment(ref ReadOnlySpan<char> span)
    {
        int dash = span.IndexOf('-');
        if (dash < 0)
        {
            ReadOnlySpan<char> whole = span;
            span = ReadOnlySpan<char>.Empty;
            return whole;
        }

        ReadOnlySpan<char> segment = span[..dash];
        span = span[(dash + 1)..];
        return segment;
    }

    private static ReadOnlySpan<char> TakeIdentifier(ref ReadOnlySpan<char> span)
    {
        int dot = span.IndexOf('.');
        if (dot < 0)
        {
            ReadOnlySpan<char> whole = span;
            span = ReadOnlySpan<char>.Empty;
            return whole;
        }

        ReadOnlySpan<char> identifier = span[..dot];
        span = span[(dot + 1)..];
        return identifier;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return false;
        foreach (char c in span)
        {
            if (!char.IsDigit(c))
                return false;
        }
        return true;
    }

    private static int CompareNumericIdentifier(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        ReadOnlySpan<char> trimmedX = x.TrimStart('0');
        ReadOnlySpan<char> trimmedY = y.TrimStart('0');
        if (trimmedX.Length != trimmedY.Length)
            return trimmedX.Length < trimmedY.Length ? -1 : 1;
        return trimmedX.SequenceCompareTo(trimmedY);
    }

    private static int CompareNumericParts(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        while (true)
        {
            ReadOnlySpan<char> partX = TakeNumericPart(ref x, out string? ownedX);
            ReadOnlySpan<char> partY = TakeNumericPart(ref y, out string? ownedY);
            if (partX.IsEmpty && ownedX is null && partY.IsEmpty && ownedY is null)
                return 0;

            int cmp = CompareNumericIdentifier(
                ownedX is null ? partX : ownedX,
                ownedY is null ? partY : ownedY
            );
            if (cmp != 0)
                return cmp;
        }
    }

    private static ReadOnlySpan<char> TakeNumericPart(
        ref ReadOnlySpan<char> version,
        out string? owned
    )
    {
        owned = null;
        while (!version.IsEmpty)
        {
            int dot = version.IndexOf('.');
            ReadOnlySpan<char> segment = dot < 0 ? version : version[..dot];
            version = dot < 0 ? ReadOnlySpan<char>.Empty : version[(dot + 1)..];

            if (IsAllDigits(segment))
                return segment;

            string digits = string.Concat(segment.ToArray().Where(char.IsDigit));
            if (digits.Length > 0)
            {
                owned = digits;
                return ReadOnlySpan<char>.Empty;
            }
        }

        return ReadOnlySpan<char>.Empty;
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

        int plusIndex = span.IndexOf('+');
        if (plusIndex >= 0)
            span = span[..plusIndex];

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
