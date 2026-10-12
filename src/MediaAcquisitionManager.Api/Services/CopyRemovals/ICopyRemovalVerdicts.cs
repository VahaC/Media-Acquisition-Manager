namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — reads what the removal rules judge by (<see cref="CopyRemovalJudge"/>): the replacement gates (ADR 0033) and the
/// files open replacements and open removals hold. Read fresh on every call, never cached, as the gates are.
/// </summary>
public interface ICopyRemovalVerdicts
{
    /// <summary>The judge of this moment.</summary>
    Task<CopyRemovalJudge> ReadAsync(CancellationToken cancellationToken = default);
}
