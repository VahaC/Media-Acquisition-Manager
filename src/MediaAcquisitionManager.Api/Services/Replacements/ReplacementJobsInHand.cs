using System.Collections.Concurrent;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the replacements someone in this process is writing to the media for right now (ADR 0030): the mover for
/// each job it works on, and an owner's Restore, Delete for good or Retry for its request. One at a time per job: whoever
/// takes a job first has it until it releases it, and anyone else is turned away rather than kept waiting.
/// </summary>
/// <remarks>
/// <para>
/// The state alone can't tell a write under way from one a restart cut: both leave the job <c>Quarantining</c>,
/// <c>Restoring</c> or <c>Deleting</c>. This registry can, as it lives and dies with the process: a job in one of those
/// states that no one here holds was cut — by a restart, or by a step that threw — and the mover's recovery takes it up.
/// A singleton, kept in memory only, never in the database: a row would outlive the very crash it should tell apart.
/// </para>
/// <para>
/// Thread-safe. The taker releases in a <c>finally</c>, so a write that throws never leaves its job held.
/// </para>
/// </remarks>
public sealed class ReplacementJobsInHand
{
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    /// <summary>Takes the replacement <paramref name="replacementJobId"/>; <c>false</c> when someone already holds it.</summary>
    public bool TryTake(Guid replacementJobId) => _held.TryAdd(replacementJobId, 0);

    /// <summary>Lets the replacement <paramref name="replacementJobId"/> go; nothing when no one holds it.</summary>
    public void Release(Guid replacementJobId) => _held.TryRemove(replacementJobId, out _);

    /// <summary>Whether someone in this process holds the replacement <paramref name="replacementJobId"/> now.</summary>
    public bool Holds(Guid replacementJobId) => _held.ContainsKey(replacementJobId);
}
