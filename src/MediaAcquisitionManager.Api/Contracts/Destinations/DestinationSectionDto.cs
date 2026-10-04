namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One section of a release source, as the Add or Edit destination dialog lists it to map
/// (MAM-32).</summary>
/// <remarks>
/// MAM-32 — the ids a source files under one name are one section, unless they are mapped to different
/// destinations or only some are mapped: then each part is a section of its own, so what is listed is
/// exactly what is stored (<c>DestinationSectionGrouping</c>).
/// </remarks>
/// <param name="Name">Its name, without the gateway's subsection mark («Мультсеріали», not
/// «|-Мультсеріали»).</param>
/// <param name="CategoryIds">The source's ids filed under it, in the source's order; what a save sends
/// back.</param>
/// <param name="IsSourceSpecific">Whether it is the source's own section rather than a standard category
/// every source shares ("TV/HD"); mapped, the source's own wins.</param>
/// <param name="DestinationId">The destination its releases go to; <c>null</c> when it is mapped to
/// none.</param>
/// <param name="DestinationName">That destination's name; <c>null</c> when it is mapped to none.</param>
public sealed record DestinationSectionDto(
    string Name,
    IReadOnlyList<string> CategoryIds,
    bool IsSourceSpecific,
    Guid? DestinationId,
    string? DestinationName);
