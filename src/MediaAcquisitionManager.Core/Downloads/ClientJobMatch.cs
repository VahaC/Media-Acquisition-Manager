using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>Which of a client's jobs is a submission's (MAM-13).</summary>
/// <param name="Status">How the hashes matched.</param>
/// <param name="Job">The one job they matched; <c>null</c> unless <see cref="ClientJobMatchStatus.Found"/>.</param>
public sealed record ClientJobMatch(ClientJobMatchStatus Status, ClientJob? Job)
{
    /// <summary>No job matched.</summary>
    public static ClientJobMatch None { get; } = new(ClientJobMatchStatus.None, null);

    /// <summary>More than one job matched.</summary>
    public static ClientJobMatch Ambiguous { get; } = new(ClientJobMatchStatus.Ambiguous, null);

    /// <summary>Exactly <paramref name="job"/> matched.</summary>
    public static ClientJobMatch Found(ClientJob job) => new(ClientJobMatchStatus.Found, job);
}
