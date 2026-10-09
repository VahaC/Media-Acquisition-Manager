using MediaAcquisitionManager.Api.Services.Search;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — what a matched release offers to replace (ADR 0032, ADR 0033): each matched work with its copies — a film's
/// every file of every version, a series' held episodes the release claims —, and why a work or a copy can't be ticked.
/// From the database and the disk alone, never a download client: the Download dialog's read and the send's plan read
/// it the same way (R1), so neither can judge a copy differently.
/// </summary>
/// <remarks>
/// <para>
/// A copy's reasons, the first that applies: no path in the app or outside the allowed roots; a library folder holding
/// it not ticked (every one must be, R3); a TV copy whose own library item places it outside the claim; held by a
/// replacement not yet ended; not inspected; a copy of another matched work that can't be replaced; then — read fresh
/// from the disk, only for a copy that passed every stored check — missing, unreadable, or changed since its last
/// inspection. One file listed under several works takes the strictest reason everywhere it is listed.
/// </para>
/// <para>
/// Every id list reaches SQLite as one JSON parameter (<c>EF.Parameter</c>): a whole series claimed is thousands of
/// episodes, versions and files.
/// </para>
/// </remarks>
public interface IReplacementCandidateReader
{
    /// <summary>
    /// The works <paramref name="matched"/> matched, in its order (strongest evidence first), each with its copies and
    /// reasons, read against <paramref name="gates"/>: the allowed roots, the ticked folders and the library folders.
    /// The switch is not part of the answer: the caller checks it first.
    /// </summary>
    Task<IReadOnlyList<ReplacementCandidateWork>> ReadAsync(
        MatchedRelease matched, ReplacementGateState gates, CancellationToken cancellationToken = default);
}
