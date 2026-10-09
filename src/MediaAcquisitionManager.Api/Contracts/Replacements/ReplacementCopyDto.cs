using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — one old copy the owner may tick for replacement (ADR 0032), or why not.</summary>
/// <param name="File">The file as the Library page shows it to the owner. Its id is what a schedule sends: one file
/// several works list carries one id under each.</param>
/// <param name="VersionId">The version holding it.</param>
/// <param name="EditionLabel">The library's own label for that version; <c>null</c> when it gives none.</param>
/// <param name="Editions">MAM-37 — the edition words its name and its version's label hold.</param>
/// <param name="DynamicRanges">The dynamic ranges ffprobe measured in its video; none when unknown.</param>
/// <param name="OtherNames">The file has more names than this one (a hardlink): moving it frees no space. Said of a copy
/// that can be ticked.</param>
/// <param name="Unavailable">Why it can't be ticked; <c>null</c> when it can.</param>
/// <param name="Holds">ADR 0031 — the torrents that list it; never a reason it can't be ticked, and empty unless the
/// torrents were read.</param>
public sealed record ReplacementCopyDto(
    LibraryFileDto File,
    Guid VersionId,
    string? EditionLabel,
    IReadOnlyList<MediaEdition> Editions,
    IReadOnlyList<DynamicRange> DynamicRanges,
    bool OtherNames,
    ReplacementCopyUnavailableReason? Unavailable,
    IReadOnlyList<ReplacementHoldDto> Holds);
