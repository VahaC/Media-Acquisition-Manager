using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — the registry keys of a library work (ADR 0017): what <see cref="DuplicateGrouping"/> joins works across
/// libraries and integrations by, as two libraries' copies of one film are two works (ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// IMDb numbers films, shows and episodes in one space, so an IMDb id is one key whatever the work's kind: a film item
/// that is an episode and the library's episode with that IMDb id are one work. TMDb numbers films and shows apart, and
/// TVDB shows, episodes and films, so their ids are keys of the work's kind — a film's TMDb 1399 never meets a show's.
/// </para>
/// <para>
/// An episode is also known by its show's ids with its season and episode, and so is a film item an episode-to-show
/// link places (MAM-34): the link's TMDb show with its numbers is the same key as the show's own TMDb TV id with a real
/// episode's numbers, so the film item linked as S01E08 and the library's own S01E08 are one work. What the library
/// says each item is stays as it is; the keys only show them as copies (ADR 0028).
/// </para>
/// </remarks>
public static class RegistryKeys
{
    /// <summary>
    /// The keys of a work of <paramref name="kind"/> with <paramref name="identifiers"/> of its own and the show
    /// <paramref name="places"/> it sits at; a blank value gives none, and each key is given once.
    /// </summary>
    public static IReadOnlyList<RegistryKey> Of(
        MediaWorkKind kind, IReadOnlyList<MediaIdentifier> identifiers, IReadOnlyList<DuplicateEpisodePlace> places)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(places);

        var own = identifiers.Select(identifier => new RegistryKey(
            identifier.Scheme,
            identifier.Value.Trim(),
            identifier.Scheme == MediaIdentifierScheme.Imdb ? null : kind,
            null,
            null));
        var series = places.SelectMany(place => place.SeriesIdentifiers.Select(identifier => new RegistryKey(
            identifier.Scheme, identifier.Value.Trim(), null, place.SeasonNumber, place.EpisodeNumber)));
        return [.. own.Concat(series).Where(key => key.Value.Length > 0).DistinctBy(DuplicateGrouping.Normalized)];
    }
}
