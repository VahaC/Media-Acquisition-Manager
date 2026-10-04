namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>One section of a release source, as the Destinations dialog lists it to map (MAM-32).</summary>
/// <param name="Name">Its name, cleaned of the gateway's subsection mark
/// (<see cref="DestinationSectionRules.CleanName"/>).</param>
/// <param name="CategoryIds">The source's ids filed under that name, in the source's order; never
/// empty.</param>
/// <param name="IsSourceSpecific">Whether it is the source's own section rather than a standard category
/// (ADR 0020): mapped, it wins over a standard one.</param>
/// <param name="DestinationId">The destination its ids are mapped to; <c>null</c> when they are mapped to
/// none.</param>
public sealed record DestinationSectionGroup(string Name, IReadOnlyList<string> CategoryIds, bool IsSourceSpecific, Guid? DestinationId);
