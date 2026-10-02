using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Finds the client's job a submission became, by the info hashes worked out from its payload (MAM-13,
/// specification §3.2, step 6) — never by name, which two releases can share and a client can change.
/// </summary>
public static class ClientJobCorrelation
{
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
}
