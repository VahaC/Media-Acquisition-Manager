using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — what the app has sent into a link's library item since it last put the item back, read from the link's
/// audit rows (decision 13: a restore is "taken from the audit row"): what the item held when the app first wrote it,
/// what the app last wrote, and whether the app added the IMDb id.
/// </summary>
/// <param name="Before">The item's ids as the app found them: the Before of the link's first <c>LibraryIdsWritten</c> row
/// (R11) — what a restore puts back.</param>
/// <param name="LastWritten">What the app last wrote, the value a restore expects to find (R11), so it never touches an id
/// someone changed since: per scheme, the After of the latest <c>LibraryIdsWritten</c> row that changed it. A row's After
/// is what the item held once the write was done — read again when the write was confirmed — so a scheme the write left
/// alone shows whatever the item held, which is never the app's.</param>
/// <param name="AddedImdb">Whether a write added the IMDb id (its Before had none, its After has one): only then is the
/// IMDb id the app's to take away or change (R11).</param>
public sealed record LibraryLinkWriteHistory(
    IReadOnlyList<MediaIdentifier> Before,
    IReadOnlyList<MediaIdentifier> LastWritten,
    bool AddedImdb)
{
    /// <summary>
    /// The history of <paramref name="link"/> since the app last put its item back; <c>null</c> when the app has sent
    /// nothing since.
    /// </summary>
    /// <remarks>
    /// R10 — a write cut by a restart left no row of its own, though the item may hold it: the reconciler records one for
    /// it (<see cref="CutWriteAsync"/>), so the history never depends on anything that later changes the link.
    /// </remarks>
    public static async Task<LibraryLinkWriteHistory?> ReadAsync(ApplicationDbContext db, LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        var rows = await RowsSinceRestoreAsync(db, link, cancellationToken);
        return Of(rows.Where(row => row.Action == AuditAction.LibraryIdsWritten).ToList());
    }

    /// <summary>
    /// R10 — the ids of the <c>LibraryIdsWritten</c> row a write a restart cut gets: the write may have reached the
    /// library, and a removal starts from what was sent. Before is what the link's latest <c>LibraryLinkSet</c> or
    /// <c>LibraryLinkReplaced</c> row found the item holding; After what the write meant to leave — the edits it was
    /// built from (<see cref="LibraryLinkEdits.Write"/>) made on Before. <c>null</c> when the link's latest save is
    /// already followed by a write's row (a Retry or a removal of a link written before — what they send was recorded),
    /// when there is no such save, or when the write had nothing to change, so nothing was posted.
    /// </summary>
    public static async Task<IReadOnlyList<AuditIdentifier>?> CutWriteAsync(ApplicationDbContext db, LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        var rows = await RowsSinceRestoreAsync(db, link, cancellationToken);
        var saved = rows.FindLastIndex(row => row.Action is AuditAction.LibraryLinkSet or AuditAction.LibraryLinkReplaced);
        if (saved < 0 || rows.Skip(saved + 1).Any(row => row.Action == AuditAction.LibraryIdsWritten))
            return null;

        // What the write edits, as the adapter reads it back (its Before lists the edited schemes only).
        var edits = LibraryLinkEdits.Write(link, Of(rows.Where(row => row.Action == AuditAction.LibraryIdsWritten).ToList()));
        List<MediaIdentifier> before = [.. rows[saved].Before.Where(identifier => edits.Any(edit => edit.Scheme == identifier.Scheme))];
        var after = LibraryLinkEdits.Intended(before, edits);
        return Changes(before, after).Any() ? LibraryLinkEdits.AuditIdentifiers(before, after) : null;
    }

    /// <summary>The history the write rows make; <c>null</c> for none.</summary>
    private static LibraryLinkWriteHistory? Of(List<AuditRow> writes)
    {
        if (writes.Count == 0)
            return null;

        // The latest change of each scheme, newest row first: a row that left a scheme as it found it says nothing of it.
        var lastWritten = new Dictionary<MediaIdentifierScheme, string?>();
        foreach (var write in Enumerable.Reverse(writes))
        {
            foreach (var (scheme, value) in Changes(write.Before, write.After))
                lastWritten.TryAdd(scheme, value);
        }

        return new LibraryLinkWriteHistory(
            writes[0].Before,
            [.. lastWritten.Where(written => written.Value is not null).Select(written => new MediaIdentifier(written.Key, written.Value!))],
            writes.Any(write => !HasImdb(write.Before) && HasImdb(write.After)));
    }

    /// <summary>The schemes <paramref name="after"/> holds otherwise than <paramref name="before"/>, each with its value
    /// after (<c>null</c>: removed), in the order they first appear.</summary>
    private static IEnumerable<(MediaIdentifierScheme Scheme, string? Value)> Changes(IReadOnlyList<MediaIdentifier> before, IReadOnlyList<MediaIdentifier> after) =>
        after.Concat(before)
            .Select(identifier => identifier.Scheme)
            .Distinct()
            .Select(scheme => (Scheme: scheme, Before: ValueOf(before, scheme), After: ValueOf(after, scheme)))
            .Where(change => !string.Equals(change.Before?.Trim(), change.After?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(change => (change.Scheme, change.After));

    /// <summary>
    /// The link's set, replace, write and restore rows since its item was last put back (a restore put the item back as
    /// the app found it: only what came after it counts), oldest first, each with its ids.
    /// </summary>
    private static async Task<List<AuditRow>> RowsSinceRestoreAsync(ApplicationDbContext db, LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        var events = (await db.AuditEvents
                .AsNoTracking()
                .Where(auditEvent => auditEvent.TargetKind == AuditTargetKind.LibraryLink
                    && auditEvent.TargetId == link.Id
                    && (auditEvent.Action == AuditAction.LibraryLinkSet
                        || auditEvent.Action == AuditAction.LibraryLinkReplaced
                        || auditEvent.Action == AuditAction.LibraryIdsWritten
                        || auditEvent.Action == AuditAction.LibraryIdsRestored))
                .Select(auditEvent => new { auditEvent.Id, auditEvent.Action, auditEvent.CreatedUtc })
                .ToListAsync(cancellationToken))
            // Ties in time (one request records a set and a write, or a replace and a restore, in one instant) are broken
            // by the action, whose numbers follow the order a request records them in.
            .OrderBy(auditEvent => auditEvent.CreatedUtc)
            .ThenBy(auditEvent => auditEvent.Action)
            .ThenBy(auditEvent => auditEvent.Id)
            .ToList();
        var sinceRestore = events.Skip(events.FindLastIndex(auditEvent => auditEvent.Action == AuditAction.LibraryIdsRestored) + 1).ToList();
        if (sinceRestore.Count == 0)
            return [];

        List<Guid> eventIds = [.. sinceRestore.Select(auditEvent => auditEvent.Id)];
        var identifiers = (await db.AuditEventIdentifiers
                .AsNoTracking()
                .Where(identifier => eventIds.Contains(identifier.AuditEventId))
                .ToListAsync(cancellationToken))
            .OrderBy(identifier => identifier.Position)
            .ToLookup(identifier => identifier.AuditEventId);
        IReadOnlyList<MediaIdentifier> IdsOf(Guid eventId, AuditIdentifierRole role) =>
            [.. identifiers[eventId].Where(identifier => identifier.Role == role).Select(identifier => new MediaIdentifier(identifier.Scheme, identifier.Value))];

        return [.. sinceRestore.Select(auditEvent => new AuditRow(auditEvent.Action, IdsOf(auditEvent.Id, AuditIdentifierRole.Before), IdsOf(auditEvent.Id, AuditIdentifierRole.After)))];
    }

    private static string? ValueOf(IReadOnlyList<MediaIdentifier> identifiers, MediaIdentifierScheme scheme) =>
        identifiers.FirstOrDefault(identifier => identifier.Scheme == scheme)?.Value;

    private static bool HasImdb(IReadOnlyList<MediaIdentifier> identifiers) =>
        identifiers.Any(identifier => identifier.Scheme == MediaIdentifierScheme.Imdb);

    /// <summary>One of the link's audit rows: its action and the ids it found and left.</summary>
    private sealed record AuditRow(AuditAction Action, IReadOnlyList<MediaIdentifier> Before, IReadOnlyList<MediaIdentifier> After);
}
