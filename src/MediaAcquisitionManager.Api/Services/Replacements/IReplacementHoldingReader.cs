namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — which torrents hold the old copies the owner may tick (ADR 0031): every download client's whole listing,
/// each torrent's files placed in the app's folders through the client's mappings, and the files there confirmed by
/// identity — read by path, confirmed by identity. Only behind the replacement gates, on the Download dialog's word.
/// </summary>
/// <remarks>
/// <para>
/// Every download client is answered for: the usable ones are asked at once, one request each; a switched-off one, one
/// of a type this version can't use, and one whose secret can't be opened are each "not read" with the app's sentence —
/// such a client may still run and seed, so its silence is never an all-clear. A client's own failure, a listing too
/// large to read and a version too old to list the files are "not read" the same way.
/// </para>
/// <para>
/// A torrent whose files can't all be placed — no save path, no metadata yet, a path no mapping covers or two cover
/// differently, a file of unknown size, a file the disk doesn't show — makes its client "read in part". A torrent file
/// outside the allowed roots is not looked at: the app may not read there, and a copy's other names are marked by its
/// link count instead. MAM-26 — a torrent file at a copy's own path holds it whatever size it lists. MAM-28 — but right
/// before a move, such a file of a client the app sent a replacement to, of a name's file name, of no size, or of its size
/// unless the name's file has several names, makes that client "read in part" too: a mapping deleted or pointed elsewhere
/// since may put a name there.
/// </para>
/// <para>
/// Reads only: the clients are listed and nothing else, and the disk is only looked at. Never throws but for the
/// caller's cancellation.
/// </para>
/// </remarks>
public interface IReplacementHoldingReader
{
    /// <summary>
    /// The torrents holding each of <paramref name="copies"/> — the ones the owner may tick, each with its identity read
    /// fresh — and how each client was read, looking only inside <paramref name="allowedRoots"/>.
    /// </summary>
    Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<ReplacementCandidateCopy> copies, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-26 — the torrents holding each of <paramref name="targets"/> — the names check 6 asks about right before a move
    /// (ADR 0031), each with its identity read fresh — and how each client was read, looking only inside
    /// <paramref name="allowedRoots"/>. The dialog's read is this one, its copies taken as the names, but for two things.
    /// This one leaves out, unasked and unlisted, every client whose path mappings reach none of
    /// <paramref name="allowedRoots"/> (<c>ClientFilePlaces.Reaches</c>) — ADR 0031's "each download client whose paths
    /// reach the allowed folders" —, where the dialog answers for every client. And MAM-28 — every client of
    /// <paramref name="libraryClients"/>, the ones the app sent a replacement to, is asked whatever its mappings say: a
    /// torrent of one listing, outside <paramref name="allowedRoots"/>, a file of a name's file name (in any letter case),
    /// of no size, or of its size unless the name's file has several names — then it is the torrent's own name of it,
    /// which the move leaves seeding — makes it "read in part" (<see cref="ReplacementClientReading.OutsideTorrents"/>), as
    /// a mapping deleted or pointed elsewhere since may hide a name there.
    /// </summary>
    Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<ReplacementHoldingTarget> targets,
        IReadOnlyList<string> allowedRoots,
        IReadOnlyCollection<Guid> libraryClients,
        CancellationToken cancellationToken = default);
}
