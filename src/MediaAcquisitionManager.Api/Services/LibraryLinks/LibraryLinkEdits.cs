using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — which ids a write sends into a link's library item, and which a restore sends back (R11): the one place the
/// rules live, so a write, a replace and a removal can't disagree about what is the app's to change.
/// </summary>
/// <remarks>
/// Only what the app wrote is ever the app's: the TMDb id is the link's to set (<see cref="LibraryIdentifierEditRule.Always"/>),
/// but an IMDb id the library already holds is the library's (<see cref="LibraryIdentifierEditRule.OnlyWhereAbsent"/>),
/// and anything taken back is taken back only where the item still holds what the app wrote
/// (<see cref="LibraryIdentifierEditRule.OnlyWhereExpected"/>, decision 13) — a value someone changed since stays.
/// </remarks>
public static class LibraryLinkEdits
{
    /// <summary>
    /// The edits that put <paramref name="link"/> into its item. A link to the work: its TMDb id always, and the IMDb id
    /// TMDb gave for the film or show. An episode's link: the episode's own IMDb id (<c>EpisodeImdbId</c>) — never a TMDb
    /// id, which on a film item names a film —, after the film id the app wrote for the item before is put back (a film
    /// link replaced by the episode's, R11). An IMDb id goes only where the item holds none — or, when the app added
    /// another IMDb id to the item earlier (<paramref name="history"/>), in place of the one it added (removed when the
    /// link has none now), and only while the item still holds that one.
    /// </summary>
    /// <remarks>
    /// The IMDb id the app added that is the link's own still goes only where the item holds none: Retry and Write again
    /// put it back when the library lost it, and leave one someone set since. Made "only where the item holds it", the
    /// edit could never change anything, and the link would settle written without its IMDb id.
    /// </remarks>
    public static IReadOnlyList<LibraryIdentifierEdit> Write(LibraryItemLinkEntity link, LibraryLinkWriteHistory? history)
    {
        List<LibraryIdentifierEdit> edits = link.Target == LibraryLinkTarget.Work
            ? [new(link.Scheme, link.Value, LibraryIdentifierEditRule.Always)]
            : history is null ? [] : [.. RestoreTmdb(history)];
        var imdbId = link.Target == LibraryLinkTarget.Work ? link.ImdbId : link.EpisodeImdbId;
        if (history is { AddedImdb: true }
            && ValueOf(history.LastWritten, MediaIdentifierScheme.Imdb) is { } addedImdb
            && !SameValue(addedImdb, imdbId))
            edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, imdbId, LibraryIdentifierEditRule.OnlyWhereExpected, addedImdb));
        else if (imdbId is not null)
            edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, imdbId, LibraryIdentifierEditRule.OnlyWhereAbsent));
        return edits;
    }

    /// <summary>
    /// The edits that put the item back as the app found it (R11): the film's or the show's TMDb id the app last wrote goes
    /// back to what the item held before the app's first write (removed when it held none), and the IMDb id the app added
    /// is removed — each only while the item still holds what the app wrote. Empty when there is nothing to put back.
    /// </summary>
    /// <remarks>
    /// Read from <paramref name="history"/>, not from the link: a link replaced by an episode's link to its show names the
    /// show now, while the item holds the film id the app wrote before. A series item's TMDb id is a show's
    /// (<see cref="MediaIdentifierScheme.TmdbTv"/>, ADR 0017), as the library reads it back, and a movie item's a film's:
    /// one item's history holds only its own. An episode's link wrote only the episode's IMDb id, so its removal takes
    /// back that one alone.
    /// </remarks>
    public static IReadOnlyList<LibraryIdentifierEdit> Restore(LibraryLinkWriteHistory history)
    {
        List<LibraryIdentifierEdit> edits = [.. RestoreTmdb(history)];
        if (history.AddedImdb && ValueOf(history.LastWritten, MediaIdentifierScheme.Imdb) is { } addedImdb)
            edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, null, LibraryIdentifierEditRule.OnlyWhereExpected, addedImdb));
        return edits;
    }

    /// <summary>
    /// The id a sync judges <paramref name="link"/> by and a write leaves in its item — its "written key": a link to the
    /// work, its own TMDb id; an episode's link, the episode's IMDb id once TMDb gave one. <c>null</c> for a link that
    /// writes nothing of its own (an episode's without its IMDb id).
    /// </summary>
    public static MediaIdentifier? WrittenKey(LibraryItemLinkEntity link) =>
        link.Target == LibraryLinkTarget.Work
            ? new MediaIdentifier(link.Scheme, link.Value)
            : link.EpisodeImdbId is { } episodeImdbId ? new MediaIdentifier(MediaIdentifierScheme.Imdb, episodeImdbId) : null;

    /// <summary>Whether <paramref name="identifiers"/> hold <paramref name="key"/>: its scheme's first value is its value
    /// (trimmed, in any letter case), as a sync reads the item.</summary>
    public static bool Holds(IReadOnlyList<MediaIdentifier> identifiers, MediaIdentifier key) =>
        SameValue(ValueOf(identifiers, key.Scheme), key.Value);

    /// <summary>
    /// What the item holds after <paramref name="write"/> as the audit row records it (R10): what the read after the
    /// write showed when it confirmed the change, else what the change meant to leave — the ids read before it with each
    /// applied edit made.
    /// </summary>
    public static IReadOnlyList<MediaIdentifier> After(LibraryIdentifierWrite write, IReadOnlyList<LibraryIdentifierEdit> edits)
    {
        if (write.Confirmed)
            return write.After;

        var applied = edits.Where(edit => write.Applied.Contains(edit.Scheme)).ToList();
        return
        [
            .. write.Before.Where(identifier => applied.All(edit => edit.Scheme != identifier.Scheme)),
            .. applied.Where(edit => edit.Value is not null).Select(edit => new MediaIdentifier(edit.Scheme, edit.Value!)),
        ];
    }

    /// <summary>
    /// What <paramref name="edits"/> would leave of an item holding <paramref name="before"/>: each edit whose rule holds
    /// made, as <see cref="LibraryIdentifierEditRule"/> says the library makes it — R10's record of a write a restart cut,
    /// whose outcome nobody read.
    /// </summary>
    public static IReadOnlyList<MediaIdentifier> Intended(IReadOnlyList<MediaIdentifier> before, IReadOnlyList<LibraryIdentifierEdit> edits)
    {
        var made = edits
            .Where(edit =>
            {
                var current = ValueOf(before, edit.Scheme);
                var holds = edit.Rule switch
                {
                    LibraryIdentifierEditRule.OnlyWhereAbsent => string.IsNullOrWhiteSpace(current),
                    LibraryIdentifierEditRule.OnlyWhereExpected => SameValue(current, edit.Expected),
                    _ => true,
                };
                return holds && !SameValue(current, edit.Value);
            })
            .ToList();
        return
        [
            .. before.Where(identifier => made.All(edit => edit.Scheme != identifier.Scheme)),
            .. made.Where(edit => edit.Value is not null).Select(edit => new MediaIdentifier(edit.Scheme, edit.Value!)),
        ];
    }

    /// <summary>An audit row's ids: <paramref name="before"/> as what the change found, <paramref name="after"/> as what it
    /// left, in their order.</summary>
    public static IReadOnlyList<AuditIdentifier> AuditIdentifiers(IEnumerable<MediaIdentifier> before, IEnumerable<MediaIdentifier> after) =>
    [
        .. before.Select(identifier => new AuditIdentifier(AuditIdentifierRole.Before, identifier.Scheme, identifier.Value)),
        .. after.Select(identifier => new AuditIdentifier(AuditIdentifierRole.After, identifier.Scheme, identifier.Value)),
    ];

    // The film's or the show's TMDb id the app last wrote put back to what the item held before (R11).
    private static IEnumerable<LibraryIdentifierEdit> RestoreTmdb(LibraryLinkWriteHistory history)
    {
        foreach (var scheme in (MediaIdentifierScheme[])[MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.TmdbTv])
        {
            if (ValueOf(history.LastWritten, scheme) is not { } written)
                continue;
            var before = ValueOf(history.Before, scheme);
            if (!SameValue(before, written))
                yield return new LibraryIdentifierEdit(scheme, before, LibraryIdentifierEditRule.OnlyWhereExpected, written);
        }
    }

    private static string? ValueOf(IReadOnlyList<MediaIdentifier> identifiers, MediaIdentifierScheme scheme) =>
        identifiers.FirstOrDefault(identifier => identifier.Scheme == scheme)?.Value;

    private static bool SameValue(string? first, string? second) =>
        string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
}
