using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What a stored search's releases claim, every source together (MAM-12): the choices the release
/// list's language and quality filters — MAM-32, and its kind filter — offer, so none lists nothing.</summary>
/// <param name="AudioLanguages">Every audio language a release's title claims, as lower-case ISO 639-2
/// codes, in ordinal order.</param>
/// <param name="HasUnknownAudioLanguage">Whether a release's title claims no audio language (the
/// <c>unknown</c> filter).</param>
/// <param name="Resolutions">Every resolution class a release's title claims, highest first.</param>
/// <param name="HasUnknownResolution">Whether a release's title claims no resolution (the <c>unknown</c>
/// filter).</param>
/// <param name="Kinds">MAM-32 — every kind of release the sources listed, <c>Movie</c> before <c>Tv</c>:
/// the choices the list's kind filter offers.</param>
public sealed record SearchFacetsDto(
    IReadOnlyList<string> AudioLanguages,
    bool HasUnknownAudioLanguage,
    IReadOnlyList<int> Resolutions,
    bool HasUnknownResolution,
    IReadOnlyList<ReleaseKind> Kinds);
