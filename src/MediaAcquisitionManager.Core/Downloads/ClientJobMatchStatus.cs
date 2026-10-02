namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>How a submission's info hashes matched the client's jobs (MAM-13).</summary>
public enum ClientJobMatchStatus
{
    /// <summary>Exactly one job holds them: the submission's.</summary>
    Found = 1,

    /// <summary>No job holds them, or the submission has no info hash to look for.</summary>
    None = 2,

    /// <summary>
    /// More than one job holds them, so which one is the submission's is not the app's to guess: the job
    /// needs linking by hand (specification §3.2, step 6).
    /// </summary>
    Ambiguous = 3,
}
