using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — what of a release the owner may replace, or why there is nothing to read.</summary>
/// <param name="Status">How the read went.</param>
/// <param name="Options">The answer; <c>null</c> unless <see cref="ReplacementOptionsStatus.Listed"/>.</param>
/// <param name="Error">A readable sentence; <c>null</c> when listed.</param>
public sealed record ReplacementOptionsResult(ReplacementOptionsStatus Status, ReplacementOptionsResponse? Options, string? Error)
{
    public static ReplacementOptionsResult Listed(ReplacementOptionsResponse options) => new(ReplacementOptionsStatus.Listed, options, null);

    public static ReplacementOptionsResult Invalid(string error) => new(ReplacementOptionsStatus.Invalid, null, error);

    public static ReplacementOptionsResult ReleaseMissing(string error) => new(ReplacementOptionsStatus.ReleaseMissing, null, error);

    public static ReplacementOptionsResult DestinationMissing(string error) => new(ReplacementOptionsStatus.DestinationMissing, null, error);
}
