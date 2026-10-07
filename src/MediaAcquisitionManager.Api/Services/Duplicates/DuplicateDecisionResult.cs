namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-21 — the outcome of <see cref="IDuplicateDecisionService"/>'s calls.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">The sentence when <see cref="DuplicateDecisionStatus.Invalid"/>.</param>
public sealed record DuplicateDecisionResult(DuplicateDecisionStatus Status, string? Error)
{
    public static DuplicateDecisionResult Done { get; } = new(DuplicateDecisionStatus.Done, null);

    public static DuplicateDecisionResult NotFound { get; } = new(DuplicateDecisionStatus.NotFound, null);

    public static DuplicateDecisionResult Invalid(string error) => new(DuplicateDecisionStatus.Invalid, error);
}
