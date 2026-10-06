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
    /// The edits that put <paramref name="link"/> into its item: its TMDb id always; the IMDb id TMDb gave only where the
    /// item holds none — or, when the app added another IMDb id to the item earlier (<paramref name="history"/>), in place
    /// of the one it added (removed when the link has none now), and only while the item still holds that one.
    /// </summary>
    /// <remarks>
    /// The IMDb id the app added that is the link's own still goes only where the item holds none: Retry and Write again
    /// put it back when the library lost it, and leave one someone set since. Made "only where the item holds it", the
    /// edit could never change anything, and the link would settle written without its IMDb id.
    /// </remarks>
    public static IReadOnlyList<LibraryIdentifierEdit> Write(LibraryItemLinkEntity link, LibraryLinkWriteHistory? history)
    {
        List<LibraryIdentifierEdit> edits = [new(link.Scheme, link.Value, LibraryIdentifierEditRule.Always)];
        if (history is { AddedImdb: true }
            && ValueOf(history.LastWritten, MediaIdentifierScheme.Imdb) is { } addedImdb
            && !SameValue(addedImdb, link.ImdbId))
            edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, link.ImdbId, LibraryIdentifierEditRule.OnlyWhereExpected, addedImdb));
        else if (link.ImdbId is { } imdbId)
            edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, imdbId, LibraryIdentifierEditRule.OnlyWhereAbsent));
        return edits;
    }

    /// <summary>
    /// The edits that put the item back as the app found it (R11): the film's TMDb id the app last wrote goes back to what
    /// the item held before the app's first write (removed when it held none), and the IMDb id the app added is removed —
    /// each only while the item still holds what the app wrote. Empty when there is nothing to put back.
    /// </summary>
    /// <remarks>
    /// Read from <paramref name="history"/>, not from the link: a link replaced by an episode's link to its show names the
    /// show now, while the item holds the film id the app wrote before. A film's TMDb id only: the one library that writes
    /// ids writes films alone (Jellyfin's <c>WritableKinds</c>, ADR 0026), so no show's id was ever sent to put back — a
    /// library that writes shows would need <see cref="MediaIdentifierScheme.TmdbTv"/> here too.
    /// </remarks>
    public static IReadOnlyList<LibraryIdentifierEdit> Restore(LibraryLinkWriteHistory history)
    {
        var edits = new List<LibraryIdentifierEdit>();
        if (ValueOf(history.LastWritten, MediaIdentifierScheme.Tmdb) is { } writtenTmdb)
        {
            var before = ValueOf(history.Before, MediaIdentifierScheme.Tmdb);
            if (!SameValue(before, writtenTmdb))
                edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, before, LibraryIdentifierEditRule.OnlyWhereExpected, writtenTmdb));
        }

        if (history.AddedImdb && ValueOf(history.LastWritten, MediaIdentifierScheme.Imdb) is { } addedImdb)
            edits.Add(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, null, LibraryIdentifierEditRule.OnlyWhereExpected, addedImdb));
        return edits;
    }

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

    private static string? ValueOf(IReadOnlyList<MediaIdentifier> identifiers, MediaIdentifierScheme scheme) =>
        identifiers.FirstOrDefault(identifier => identifier.Scheme == scheme)?.Value;

    private static bool SameValue(string? first, string? second) =>
        string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
}
