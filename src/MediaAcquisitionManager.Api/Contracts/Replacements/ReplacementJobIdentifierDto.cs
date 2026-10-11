using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-27 — one registry id on the job's page (ADR 0035): an id the library gave a new file's item when the check last read
/// it, or one a work the replacement expects had when it was scheduled — what the check compared the two by.
/// </summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id, as read.</param>
/// <param name="OfSeries">A new file's: the id is its episode's series' rather than its own item's (ADR 0017: only the
/// series' say which show it is). <c>false</c> for a work's.</param>
/// <param name="SetByOwner">A work's: the id is the owner's own TMDb link rather than the library's report (ADR 0026),
/// which Jellyfin's Identify applies to the item. <c>false</c> for a new file's.</param>
public sealed record ReplacementJobIdentifierDto(MediaIdentifierScheme Scheme, string Value, bool OfSeries, bool SetByOwner);
