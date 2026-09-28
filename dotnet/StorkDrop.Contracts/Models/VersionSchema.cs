namespace StorkDrop.Contracts.Models;

/// <summary>
/// Optional, product-authored description of how a compound version string is composed, so the version
/// picker can label the cascade levels and format parts (e.g. a date). Purely additive: when absent, or
/// when a version does not match it, the picker falls back to generic segment grouping or the flat list.
/// </summary>
/// <param name="Separator">Delimiter between parts (default "-"). The first part keeps its dots (SemVer core).</param>
/// <param name="Parts">One entry per version part, in order.</param>
public sealed record VersionSchema(string Separator, VersionPart[] Parts);

/// <summary>
/// One part of a <see cref="VersionSchema"/>.
/// </summary>
/// <param name="Label">Human-readable name shown as the cascade level header (e.g. "Build", "Date").</param>
/// <param name="Format">
/// Optional display format. Currently supported: <c>date:&lt;pattern&gt;</c> (e.g. <c>date:yyyyMMdd</c>),
/// which parses the raw segment with the pattern and shows it as an ISO date. Unknown/failed formats make
/// the whole schema not apply for that channel (graceful fallback), never an error.
/// </param>
public sealed record VersionPart(string Label, string? Format = null);
