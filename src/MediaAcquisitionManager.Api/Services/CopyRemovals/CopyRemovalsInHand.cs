using MediaAcquisitionManager.Api.Services.Replacements;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the removals someone in this process is writing to the media for right now (ADR 0030): the owner's Remove, its
/// Restore or its Delete for good for its request, and the quarantine's pass for each cut removal it settles. A removal in
/// <c>Moving</c>, <c>Restoring</c> or <c>Deleting</c> that no one here holds was cut, and the pass takes it up
/// (<see cref="RowsInHand"/> says why only memory can tell).
/// </summary>
public sealed class CopyRemovalsInHand : RowsInHand;
