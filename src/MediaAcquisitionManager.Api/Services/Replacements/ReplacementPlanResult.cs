namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — what the plan of a scheduled replacement said.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Plan">What to write with the download job; <c>null</c> unless <see cref="ReplacementPlanStatus.Planned"/>.</param>
/// <param name="Error">A readable sentence; <c>null</c> with a plan.</param>
public sealed record ReplacementPlanResult(ReplacementPlanStatus Status, ReplacementPlan? Plan, string? Error)
{
    public static ReplacementPlanResult Planned(ReplacementPlan plan) => new(ReplacementPlanStatus.Planned, plan, null);

    public static ReplacementPlanResult Invalid(string error) => new(ReplacementPlanStatus.Invalid, null, error);

    public static ReplacementPlanResult Unavailable(string error) => new(ReplacementPlanStatus.Unavailable, null, error);
}
