namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One release source and its sections, as the Add or Edit destination dialog lists them
/// (MAM-32).</summary>
/// <param name="Id">The release search integration's id: what a save names a section's source by.</param>
/// <param name="Name">Its name.</param>
/// <param name="Sections">Its sections in its own order, then the mapped ones it no longer lists, by name;
/// only the mapped ones when it couldn't be asked.</param>
/// <param name="Error">The sentence saying why it couldn't be asked — switched off, a type this version of
/// the app can't search through, a stored secret it can't read, or the source's own failure; <c>null</c>
/// when it answered.</param>
public sealed record DestinationSectionSourceDto(
    Guid Id,
    string Name,
    IReadOnlyList<DestinationSectionDto> Sections,
    string? Error);
