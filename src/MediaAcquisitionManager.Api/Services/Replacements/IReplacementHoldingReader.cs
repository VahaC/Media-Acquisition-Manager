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
/// link count instead.
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
}
