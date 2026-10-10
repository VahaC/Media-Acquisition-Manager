using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — what the media library said, when it was last read, of the item at one file's path (checks 4 and 3,
/// ADR 0035): whether it lists one, what kind, whether it had finished reading the file, its ids and an episode's place.
/// </summary>
/// <param name="Kind">What the library made of the file; <c>null</c> while it lists no item for it.</param>
/// <param name="Refreshed">Whether the library had finished reading the file: it gave a running time, and an episode
/// its number. A new item is saved first without its ids and numbers, so nothing it carries counts before then.</param>
/// <param name="Identifiers">The item's own registry ids.</param>
/// <param name="SeriesItemId">An episode's series item; <c>null</c> for anything else, or when the library gives
/// none.</param>
/// <param name="SeriesIdentifiers">An episode's series' registry ids, its show's TMDb id as
/// <see cref="MediaIdentifierScheme.TmdbTv"/> (ADR 0017).</param>
/// <param name="SeasonNumber">An episode's season.</param>
/// <param name="EpisodeNumber">An episode's number in its season.</param>
/// <param name="EpisodeNumberEnd">The last episode a file of several covers, as the library reports it.</param>
public sealed record ReplacementLibraryReading(
    LibraryFileKind? Kind,
    bool Refreshed,
    IReadOnlyList<MediaIdentifier> Identifiers,
    string? SeriesItemId,
    IReadOnlyList<MediaIdentifier> SeriesIdentifiers,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? EpisodeNumberEnd)
{
    /// <summary>The library lists no item at the path.</summary>
    public static ReplacementLibraryReading NotListed { get; } = new(null, false, [], null, [], null, null, null);
}
