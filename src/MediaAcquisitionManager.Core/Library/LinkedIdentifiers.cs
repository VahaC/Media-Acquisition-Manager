using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — the one rule for "a linked id counts like a library id": the ids a library item counts as holding once its
/// TMDb link is applied. The sync's grouping, the Library page, its filters and search, release matching and the
/// recommendations all go through it, so none of them can count a link differently.
/// </summary>
/// <remarks>
/// <para>
/// A link to the work itself (<see cref="LibraryLinkTarget.Work"/>) adds its TMDb id, once, and the IMDb id TMDb gave —
/// only when the item's library ids hold no IMDb id of their own (R8: the library's IMDb wins). The library's id the link
/// replaces gives way while the link is not confirmed in the library (R6: kept in the app, queued, being written, not
/// written): once the library took the link's id, a different id there is the library's word again, and conflicts.
/// </para>
/// <para>
/// A library id of the link's scheme that is neither the link's nor the replaced one stays: the item then holds two
/// values of one scheme and conflicts — neither wins (<see cref="ConflictingValue"/>).
/// </para>
/// <para>
/// An episode-to-show link (<see cref="LibraryLinkTarget.ShowEpisode"/>) adds nothing — the show's ids are not the
/// item's — but drops the film id the library wrongly gave the item (R7): the link replaces it in the app, and what the
/// app writes for it is the episode's own IMDb id, never a TMDb one, so it never stops giving way, whatever the link's
/// state. Once the library holds that IMDb id, the item reports it as its own, like any library id. A film id the
/// library gives the item after the link was set — a refresh, an Identify — is neither dropped nor taken: it stays, and
/// conflicts with the link (<see cref="ConflictingValue"/>), as the owner said the item is an episode.
/// </para>
/// <para>
/// Values compare trimmed and in any letter case, as the library's own ids are grouped (ADR 0008).
/// </para>
/// </remarks>
public static class LinkedIdentifiers
{
    /// <summary>The write states in which the link isn't confirmed in the library, so the id it replaces gives way.</summary>
    private static readonly LibraryLinkWriteState[] Unconfirmed =
    [
        LibraryLinkWriteState.KeptInApp, LibraryLinkWriteState.Queued, LibraryLinkWriteState.Writing, LibraryLinkWriteState.NotWritten,
    ];

    /// <summary>The ids the item counts as holding: <paramref name="libraryIdentifiers"/> with <paramref name="link"/>
    /// applied.</summary>
    public static IReadOnlyList<MediaIdentifier> Effective(IReadOnlyList<MediaIdentifier> libraryIdentifiers, ItemLinkFacts link)
    {
        ArgumentNullException.ThrowIfNull(libraryIdentifiers);
        ArgumentNullException.ThrowIfNull(link);

        var effective = libraryIdentifiers.Where(identifier => !GivesWay(identifier, link)).ToList();
        if (link.Target != LibraryLinkTarget.Work)
            return effective;

        if (!effective.Any(identifier => identifier.Scheme == link.Scheme && SameValue(identifier.Value, link.Value)))
            effective.Add(new MediaIdentifier(link.Scheme, link.Value.Trim()));
        if (!string.IsNullOrWhiteSpace(link.ImdbId) && !libraryIdentifiers.Any(identifier => identifier.Scheme == MediaIdentifierScheme.Imdb))
            effective.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, link.ImdbId.Trim()));
        return effective;
    }

    /// <summary>
    /// The library's value that conflicts with the link, as the library stores it; <c>null</c> when there is none. For a
    /// link to the work, a value of the link's scheme that is neither the link's nor a replaced one giving way; for an
    /// episode-to-show link, a film id other than the one the link replaced — one the library gave the item later.
    /// </summary>
    public static string? ConflictingValue(IReadOnlyList<MediaIdentifier> libraryIdentifiers, ItemLinkFacts link)
    {
        ArgumentNullException.ThrowIfNull(libraryIdentifiers);
        ArgumentNullException.ThrowIfNull(link);

        return link.Target == LibraryLinkTarget.Work
            ? libraryIdentifiers
                .FirstOrDefault(identifier => identifier.Scheme == link.Scheme && !SameValue(identifier.Value, link.Value) && !GivesWay(identifier, link))
                ?.Value
            : libraryIdentifiers
                .FirstOrDefault(identifier => identifier.Scheme == MediaIdentifierScheme.Tmdb && !GivesWay(identifier, link))
                ?.Value;
    }

    // The library id the link replaces, while it gives way: of the link's scheme for a link to the work while the link
    // isn't confirmed (R6); the film id for an episode-to-show link always (R7) — the link's write is the episode's IMDb
    // id, so the film id is never what its state is about.
    private static bool GivesWay(MediaIdentifier identifier, ItemLinkFacts link) =>
        link.SupersededValue is { } superseded
        && (link.Target == LibraryLinkTarget.Work
            ? Unconfirmed.Contains(link.WriteState) && identifier.Scheme == link.Scheme
            : identifier.Scheme == MediaIdentifierScheme.Tmdb)
        && SameValue(identifier.Value, superseded);

    private static bool SameValue(string first, string second) =>
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
}
