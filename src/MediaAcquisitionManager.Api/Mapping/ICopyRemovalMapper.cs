using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>MAM-40 — turns a removal of a copy and its names into the Removed copies list's contract.</summary>
public interface ICopyRemovalMapper
{
    /// <summary>
    /// <paramref name="removal"/> with every one of its <paramref name="names"/> in their plan's order, the room its names in
    /// the quarantine take, what Delete for good frees — by each name's link count now where
    /// <paramref name="linkCountsNow"/> has it, else the one the move read —, and which actions it offers.
    /// </summary>
    CopyRemovalDto ToDto(CopyRemovalEntity removal, IReadOnlyList<CopyRemovalNameEntity> names, IReadOnlyDictionary<Guid, int> linkCountsNow);
}
