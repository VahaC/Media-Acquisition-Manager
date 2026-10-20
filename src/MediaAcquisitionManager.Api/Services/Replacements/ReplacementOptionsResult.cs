using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — what of a release the owner may replace, or why there is nothing to read.</summary>
/// <param name="Status">How the read went.</param>
/// <param name="Options">The answer; <c>null</c> unless <see cref="ReplacementOptionsStatus.Listed"/>.</param>
/// <param name="Error">Why not, as a message the controller renders (MAM-47); <c>null</c> when listed.</param>
public sealed record ReplacementOptionsResult(ReplacementOptionsStatus Status, ReplacementOptionsResponse? Options, Message? Error)
{
    public static ReplacementOptionsResult Listed(ReplacementOptionsResponse options) => new(ReplacementOptionsStatus.Listed, options, null);

    public static ReplacementOptionsResult Invalid(Message error) => new(ReplacementOptionsStatus.Invalid, null, error);

    public static ReplacementOptionsResult ReleaseMissing(Message error) => new(ReplacementOptionsStatus.ReleaseMissing, null, error);

    public static ReplacementOptionsResult DestinationMissing(Message error) => new(ReplacementOptionsStatus.DestinationMissing, null, error);
}
