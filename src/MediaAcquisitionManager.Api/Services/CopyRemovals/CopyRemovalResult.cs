using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>MAM-40 — what an owner's Remove, Restore or Delete for good of a copy answered.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Removal">The removal as it stands now, when <see cref="CopyRemovalStatus.Done"/>.</param>
/// <param name="Error">Why not, when <see cref="CopyRemovalStatus.Invalid"/> or <see cref="CopyRemovalStatus.Refused"/>, as
/// a message the Api renders (MAM-47, ADR 0042).</param>
public sealed record CopyRemovalResult(CopyRemovalStatus Status, CopyRemovalDto? Removal, Message? Error)
{
    public static CopyRemovalResult NotFound { get; } = new(CopyRemovalStatus.NotFound, null, null);

    public static CopyRemovalResult Done(CopyRemovalDto removal) => new(CopyRemovalStatus.Done, removal, null);

    public static CopyRemovalResult Invalid(Message error) => new(CopyRemovalStatus.Invalid, null, error);

    public static CopyRemovalResult Refused(Message error) => new(CopyRemovalStatus.Refused, null, error);
}
