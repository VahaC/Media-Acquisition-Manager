namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One section of a release source a destination's save maps to it (MAM-32), as the sections
/// list gave it.</summary>
/// <remarks>
/// MAM-32 — every field nullable and without <c>[Required]</c>, for the
/// <see cref="CreateDestinationRequest"/> reason. Taken as sent, without asking the source: a category it
/// doesn't list never matches a release (<c>DestinationSectionRules</c>).
/// </remarks>
/// <param name="ReleaseSourceId">The release search integration the section belongs to.</param>
/// <param name="CategoryIds">The source's ids filed under the section, each mapped here.</param>
/// <param name="Name">The section's name as the list gave it, stored so the setting still reads after the
/// source renames or drops it; cut to 200 characters.</param>
/// <param name="IsSourceSpecific">Whether it is the source's own section rather than a standard category,
/// as the list said; mapped, the source's own wins.</param>
public sealed record DestinationSectionRequest(
    Guid? ReleaseSourceId,
    IReadOnlyList<string?>? CategoryIds,
    string? Name,
    bool IsSourceSpecific = false);
