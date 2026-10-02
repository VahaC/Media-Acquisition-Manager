using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Finds the client's job a submission became, by the info hashes worked out from its payload (MAM-13,
/// specification §3.2, step 6) — never by name, which two releases can share and a client can change.
/// MAM-14 — and the job a followed download is, by the client's id stored on it as well
/// (<see cref="Follow"/>, <see cref="FollowIds"/>).
/// </summary>
public static class ClientJobCorrelation
{
    /// <summary>A v1 info hash in hexadecimal.</summary>
    private const int V1HashLength = 40;

    /// <summary>A v2 info hash in hexadecimal.</summary>
    private const int V2HashLength = 64;

    /// <summary>
    /// The one job of <paramref name="jobs"/> that holds one of <paramref name="hashes"/>; none when no job
    /// does or no hash is known; ambiguous when several distinct jobs do.
    /// </summary>
    /// <remarks>
    /// A job the client lists twice under one id is one job. Two ids holding one torrent can't happen in a
    /// client that names a torrent by its hash, but the rule is the core's, not one client's: whichever
    /// client does it gets a job to link by hand rather than a guess.
    /// </remarks>
    public static ClientJobMatch Match(IReadOnlyList<ClientJob> jobs, TorrentInfoHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(hashes);
        if (!hashes.IsKnown)
            return ClientJobMatch.None;

        var matching = jobs
            .Where(job => job.InfoHashes.Any(hashes.Contains))
            .DistinctBy(job => job.ClientJobId, StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return matching.Count switch
        {
            0 => ClientJobMatch.None,
            1 => ClientJobMatch.Found(matching[0]),
            _ => ClientJobMatch.Ambiguous,
        };
    }

    /// <summary>
    /// MAM-14 — the one job of <paramref name="jobs"/> that a followed download is: the job the client calls
    /// <paramref name="clientJobId"/>, or one that lists that id or either of <paramref name="hashes"/> among
    /// its info hashes. None when no job does or there is nothing to look for; ambiguous when several distinct
    /// jobs do.
    /// </summary>
    /// <remarks>
    /// The id alone isn't enough: a client can rename a job after it was stored — qBittorrent built on
    /// libtorrent 2 renames a hybrid torrent sent as a magnet to its v2 hash cut to 40 characters once the
    /// metadata arrives (ADR 0013) — and the hashes alone aren't either, for a job taken without them. So a job
    /// matches by any of the three, and the caller stores the id it matched under from then on. The id is
    /// compared exactly, as the client gave it; a hash in any letter case, as <see cref="Match"/> does.
    /// </remarks>
    public static ClientJobMatch Follow(IReadOnlyList<ClientJob> jobs, string? clientJobId, TorrentInfoHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(hashes);
        if (string.IsNullOrEmpty(clientJobId) && !hashes.IsKnown)
            return ClientJobMatch.None;

        var matching = jobs
            .Where(job => IsCalled(job, clientJobId) || job.InfoHashes.Any(hashes.Contains))
            .DistinctBy(job => job.ClientJobId, StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return matching.Count switch
        {
            0 => ClientJobMatch.None,
            1 => ClientJobMatch.Found(matching[0]),
            _ => ClientJobMatch.Ambiguous,
        };
    }

    /// <summary>
    /// MAM-14 — what a followed download is looked up by in a client's job list
    /// (<see cref="IDownloadClient.FindJobsAsync"/>): the stored id and both hashes, each that is an info hash
    /// — 40 or 64 ASCII hexadecimal characters — once, lower-case, in that order.
    /// </summary>
    /// <remarks>
    /// Anything else is left out rather than passed on: the client contract throws for a value that isn't an
    /// info hash, and one bad row must not stop the lookup of every other job of its client. An id that isn't a
    /// hash (another client's own job id) is still matched by <see cref="Follow"/>; it just can't be looked up.
    /// </remarks>
    public static IReadOnlyList<string> FollowIds(string? clientJobId, TorrentInfoHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        return [.. new[] { clientJobId, hashes.V1, hashes.V2 }
            .Select(value => value is { Length: V1HashLength or V2HashLength } && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
    }

    private static bool IsCalled(ClientJob job, string? clientJobId) =>
        !string.IsNullOrEmpty(clientJobId)
        && (string.Equals(job.ClientJobId, clientJobId, StringComparison.Ordinal)
            || job.InfoHashes.Contains(clientJobId, StringComparer.OrdinalIgnoreCase));
}
