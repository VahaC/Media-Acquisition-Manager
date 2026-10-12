using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.CopyRemovals;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ICopyRemovalMapper"/>
public sealed class CopyRemovalMapper : ICopyRemovalMapper
{
    public CopyRemovalDto ToDto(CopyRemovalEntity removal, IReadOnlyList<CopyRemovalNameEntity> names, IReadOnlyDictionary<Guid, int> linkCountsNow)
    {
        ArgumentNullException.ThrowIfNull(removal);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(linkCountsNow);
        int? LinkCountOf(CopyRemovalNameEntity name) => linkCountsNow.TryGetValue(name.Id, out var now) ? now : name.LinkCount;

        var ordered = names.OrderBy(name => name.Position).ToList();
        var quarantined = ordered.Where(name => name.State == ReplacementNameState.Quarantined).ToList();
        // The video's own row says where it went: the claim writes it with the removal.
        var video = ordered.FirstOrDefault(name => name.Kind == ReplacementNameKind.Video);
        return new CopyRemovalDto(
            removal.Id,
            removal.State,
            removal.Reason,
            removal.Title,
            removal.GroupClass,
            removal.AppPath,
            video?.QuarantinePath ?? "",
            quarantined.Sum(name => name.SizeBytes),
            // ADR 0031 — a file with another name keeps its data when this one goes, so it frees nothing; a count the
            // platform didn't give can't promise the room either.
            quarantined.Where(name => LinkCountOf(name) == 1).Sum(name => name.SizeBytes),
            removal.RequestedByUserName,
            removal.CreatedUtc,
            removal.StateChangedUtc,
            CopyRemovalOffers.CanRestore(
                removal.State, quarantined.Count > 0, ordered.Any(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Deleted })),
            CopyRemovalOffers.CanDeleteForGood(removal.State, quarantined.Count > 0),
            [
                .. ordered.Select(name => new ReplacementNameDto(
                    name.Id, name.Kind, name.AppPath, name.QuarantinePath, name.SizeBytes, LinkCountOf(name) > 1, name.State, name.Problem)),
            ]);
    }
}
