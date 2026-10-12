namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-40 — one name a <see cref="CopyRemovalEntity"/> moves into the quarantine: the removed copy's video, or one of its
/// own companions, with <see cref="QuarantinedNameEntity"/>'s columns — a replacement's names' own —, so the one recovery,
/// write loop and set of conditional updates of the quarantine serve both. The plan is written whole with the removal's
/// claim, before anything is renamed.
/// </summary>
/// <remarks>No <c>CreatedUtc</c>: part of its removal, written with its plan and never alone.</remarks>
public class CopyRemovalNameEntity : QuarantinedNameEntity
{
    /// <summary>The removal; the row is removed with it.</summary>
    public Guid CopyRemovalId { get; set; }
}
