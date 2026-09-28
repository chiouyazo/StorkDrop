using System.Globalization;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Splits a channel's version strings into an ordered, dependent cascade (SemVer core, then the parts after
/// the separator). Pure and side-effect free. Returns null whenever the versions are not regularly
/// groupable, so callers keep the existing flat-list behaviour - this is what keeps the feature fully
/// backward compatible. An optional <see cref="VersionSchema"/> only adds level labels and part formatting
/// (e.g. dates); if it does not match, grouping still happens generically.
/// </summary>
public static class VersionGrouping
{
    public static VersionTree? Build(IReadOnlyList<string> versions, VersionSchema? schema = null)
    {
        if (versions is null || versions.Count < 2)
            return null;

        string separator = string.IsNullOrEmpty(schema?.Separator) ? "-" : schema!.Separator;

        string[][] split = versions
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => v.Split(separator))
            .ToArray();

        if (split.Length < 2)
            return null;

        int segmentCount = split[0].Length;
        if (segmentCount < 2 || split.Any(s => s.Length != segmentCount))
            return null;

        IReadOnlyList<string>? levels = null;
        Func<int, string, string>? formatter = null;
        if (
            schema is not null
            && schema.Parts.Length == segmentCount
            && TryBuildFormatter(schema, split, segmentCount, out formatter)
        )
        {
            levels = schema.Parts.Select(p => p.Label).ToList();
        }

        IReadOnlyList<VersionNode> roots = BuildLevel(split, 0, segmentCount, separator, formatter);
        return new VersionTree(levels, roots);
    }

    private static IReadOnlyList<VersionNode> BuildLevel(
        IReadOnlyList<string[]> rows,
        int depth,
        int segmentCount,
        string separator,
        Func<int, string, string>? formatter
    )
    {
        List<VersionNode> nodes = [];
        foreach (IGrouping<string, string[]> group in rows.GroupBy(r => r[depth]))
        {
            string segment = group.Key;
            string display = formatter?.Invoke(depth, segment) ?? segment;

            if (depth == segmentCount - 1)
            {
                string full = string.Join(separator, group.First());
                nodes.Add(new VersionNode(segment, display, full, []));
            }
            else
            {
                IReadOnlyList<VersionNode> children = BuildLevel(
                    group.ToList(),
                    depth + 1,
                    segmentCount,
                    separator,
                    formatter
                );
                nodes.Add(new VersionNode(segment, display, null, children));
            }
        }

        return depth == 0
            ? nodes.OrderByDescending(n => n.Segment, VersionComparer.Instance).ToList()
            : nodes.OrderByDescending(n => n.Segment, SegmentComparer.Instance).ToList();
    }

    private static bool TryBuildFormatter(
        VersionSchema schema,
        string[][] split,
        int segmentCount,
        out Func<int, string, string>? formatter
    )
    {
        formatter = null;
        string?[] datePatterns = new string?[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            string? format = schema.Parts[i].Format;
            if (format is null || !format.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                continue;

            string pattern = format["date:".Length..];
            foreach (string[] row in split)
            {
                if (
                    !DateTime.TryParseExact(
                        row[i],
                        pattern,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out _
                    )
                )
                    return false;
            }
            datePatterns[i] = pattern;
        }

        formatter = (depth, segment) =>
            datePatterns[depth] is string pattern
            && DateTime.TryParseExact(
                segment,
                pattern,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed
            )
                ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : segment;
        return true;
    }

    /// <summary>Orders non-core segments newest-first: numerically when both parse as integers, else ordinal.</summary>
    private sealed class SegmentComparer : IComparer<string>
    {
        public static readonly SegmentComparer Instance = new SegmentComparer();

        public int Compare(string? x, string? y)
        {
            if (long.TryParse(x, out long xn) && long.TryParse(y, out long yn))
                return xn.CompareTo(yn);
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}
