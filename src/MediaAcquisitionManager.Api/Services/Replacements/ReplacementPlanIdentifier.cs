using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — one registry id of a matched work, kept with a scheduled replacement
/// (<c>ReplacementJobWorkIdentifierEntity</c>): what MAM-25 compares the new file's ids with (ADR 0032).</summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id, as read.</param>
/// <param name="SetByOwner">The owner's own TMDb link rather than a library's report (ADR 0026).</param>
public sealed record ReplacementPlanIdentifier(MediaIdentifierScheme Scheme, string Value, bool SetByOwner);
