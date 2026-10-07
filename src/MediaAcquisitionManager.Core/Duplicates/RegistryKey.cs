using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — one registry id that says which work a library work is (ADR 0017): its own id, in the number space of its
/// registry, or — an episode — its series' id with its season and episode (a series key).
/// </summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id, trimmed; compared upper-cased, as <c>WorkMatching</c> compares.</param>
/// <param name="Kind">The kind of work the number space is: the work's own for a TMDb or TVDB id, which number films,
/// shows and episodes apart; <c>null</c> for an IMDb id, one space for every kind, and for a series key.</param>
/// <param name="SeasonNumber">A series key's season; <c>null</c> for a work's own id.</param>
/// <param name="EpisodeNumber">A series key's episode; <c>null</c> for a work's own id.</param>
public sealed record RegistryKey(
    MediaIdentifierScheme Scheme, string Value, MediaWorkKind? Kind, int? SeasonNumber, int? EpisodeNumber)
{
    /// <summary>Whether this is a series' id with a season and an episode, not a work's own.</summary>
    public bool IsSeriesKey => SeasonNumber is not null;
}
