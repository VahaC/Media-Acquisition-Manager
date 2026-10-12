using System.Collections.Concurrent;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26, MAM-40 — the rows someone in this process is writing to the media for right now (ADR 0030), by id: one at a
/// time per row, whoever takes it first has it until it releases it, and anyone else is turned away rather than kept
/// waiting. <see cref="ReplacementJobsInHand"/> holds replacements, <c>CopyRemovalsInHand</c> removals — one registry each,
/// so neither's recovery reads the other's.
/// </summary>
/// <remarks>
/// <para>
/// The state alone can't tell a write under way from one a restart cut: both leave the row in a running state. This
/// registry can, as it lives and dies with the process: a row in a running state that no one here holds was cut — by a
/// restart, or by a step that threw — and the quarantine's recovery takes it up. A singleton, kept in memory only, never in
/// the database: a row would outlive the very crash it should tell apart.
/// </para>
/// <para>
/// Thread-safe. The taker releases in a <c>finally</c>, so a write that throws never leaves its row held.
/// </para>
/// </remarks>
public abstract class RowsInHand
{
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    /// <summary>Takes the row <paramref name="id"/>; <c>false</c> when someone already holds it.</summary>
    public bool TryTake(Guid id) => _held.TryAdd(id, 0);

    /// <summary>Lets the row <paramref name="id"/> go; nothing when no one holds it.</summary>
    public void Release(Guid id) => _held.TryRemove(id, out _);

    /// <summary>Whether someone in this process holds the row <paramref name="id"/> now.</summary>
    public bool Holds(Guid id) => _held.ContainsKey(id);
}
