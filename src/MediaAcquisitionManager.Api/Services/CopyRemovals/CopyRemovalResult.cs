using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>MAM-40 — what an owner's Remove, Restore or Delete for good of a copy answered.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Removal">The removal as it stands now, when <see cref="CopyRemovalStatus.Done"/>.</param>
/// <param name="Error">The sentence saying why not, when <see cref="CopyRemovalStatus.Invalid"/> or
/// <see cref="CopyRemovalStatus.Refused"/>.</param>
public sealed record CopyRemovalResult(CopyRemovalStatus Status, CopyRemovalDto? Removal, string? Error)
{
    public static CopyRemovalResult NotFound { get; } = new(CopyRemovalStatus.NotFound, null, null);

    public static CopyRemovalResult Done(CopyRemovalDto removal) => new(CopyRemovalStatus.Done, removal, null);

    public static CopyRemovalResult Invalid(string error) => new(CopyRemovalStatus.Invalid, null, error);

    public static CopyRemovalResult Refused(string error) => new(CopyRemovalStatus.Refused, null, error);
}
