using System.Collections.Concurrent;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-31 — the release sources whose last call for the recommendations failed, by integration id: the availability
/// check's search or the newest releases' read, whichever came last. The live check gave Jackett a wrong key: the
/// Integrations Test and the Search page said "refused the API key", while the Recommendations page went on showing
/// its old counts and "Not checked yet" without a word, as ADR 0024's passes only logged the status.
/// </summary>
/// <remarks>
/// <para>
/// In memory, on purpose: what a source answered last is a state of the running app, not a fact to keep. Both loops
/// ask as the app starts (ADR 0024), so a restart finds out again within a pass; a failure stays until the source's
/// next answer, which the page dates. No table, so ADR 0024's rows stay as they are.
/// </para>
/// <para>
/// A source that answered clears its failure; one the app didn't ask (no search of the kind, a type this build can't
/// use, a key it can't read) changes nothing: the Integrations and Search pages say why, and it didn't fail here.
/// The error is the source's own sentence, already masked of every secret (<c>ReleaseSources</c>).
/// </para>
/// </remarks>
public sealed class RecommendationSourceFailures(TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, RecommendationSourceFailure> _failures = new();

    /// <summary>Records how each source of <paramref name="outcomes"/> answered, now.</summary>
    public void Record(IEnumerable<ReleaseSourceOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var outcome in outcomes)
        {
            if (outcome.Status == IntegrationCallStatus.Succeeded)
                _failures.TryRemove(outcome.IntegrationId, out _);
            else if (outcome.Status is { } status)
                _failures[outcome.IntegrationId] = new RecommendationSourceFailure(status, outcome.Error, now);
        }
    }

    /// <summary>Every failure recorded, by the source's integration id: a copy, safe to read while the loops record.</summary>
    public IReadOnlyDictionary<Guid, RecommendationSourceFailure> Read() => new Dictionary<Guid, RecommendationSourceFailure>(_failures);
}

/// <summary>MAM-31 — how a release source's last call for the recommendations failed.</summary>
/// <param name="Status">How the call went.</param>
/// <param name="Error">The source's sentence, every secret masked; the owner's to read.</param>
/// <param name="FailedUtc">When.</param>
public sealed record RecommendationSourceFailure(IntegrationCallStatus Status, string? Error, DateTime FailedUtc);
