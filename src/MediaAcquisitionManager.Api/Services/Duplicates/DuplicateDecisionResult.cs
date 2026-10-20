using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-21 — the outcome of <see cref="IDuplicateDecisionService"/>'s calls.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">Why, when <see cref="DuplicateDecisionStatus.Invalid"/>, as a message the Api renders (MAM-47).</param>
public sealed record DuplicateDecisionResult(DuplicateDecisionStatus Status, Message? Error)
{
    public static DuplicateDecisionResult Done { get; } = new(DuplicateDecisionStatus.Done, null);

    public static DuplicateDecisionResult NotFound { get; } = new(DuplicateDecisionStatus.NotFound, null);

    public static DuplicateDecisionResult Invalid(Message error) => new(DuplicateDecisionStatus.Invalid, error);
}
