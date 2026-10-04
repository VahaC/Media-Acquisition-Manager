using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// The filters and the order of a stored search's release list, as <see cref="SearchReleaseListRules"/>
/// read them (MAM-12). A <c>null</c> filter, or a <c>false</c> one, filters nothing.
/// </summary>
/// <param name="SourceId">Only the releases of this search source (the search's own source row, not an
/// integration).</param>
/// <param name="Kind">MAM-32 — only the releases the source lists as this kind (ADR 0020).</param>
/// <param name="AudioLanguage">Only the releases whose title claims this audio language, a lower-case
/// ISO 639-2 code like <see cref="ClaimedReleaseQuality.AudioLanguages"/>' (<c>ukr</c>).</param>
/// <param name="WithoutAudioLanguage">Only the releases whose title claims no audio language
/// (<c>unknown</c>).</param>
/// <param name="Resolution">Only the releases whose title claims exactly this resolution class
/// (<see cref="ClaimedReleaseQuality.Resolution"/>), with no bucketing: a 640x360 frame claims 360, not
/// 480.</param>
/// <param name="WithoutResolution">Only the releases whose title claims no resolution (<c>unknown</c>).</param>
/// <param name="MinimumSizeBytes">Only the releases the listing claims at least this many bytes for; one
/// whose size is unknown is left out.</param>
/// <param name="MaximumSizeBytes">Only the releases the listing claims at most this many bytes for; one
/// whose size is unknown is left out.</param>
/// <param name="Sort">The order of the list.</param>
public sealed record SearchReleaseFilter(
    Guid? SourceId,
    ReleaseKind? Kind,
    string? AudioLanguage,
    bool WithoutAudioLanguage,
    int? Resolution,
    bool WithoutResolution,
    long? MinimumSizeBytes,
    long? MaximumSizeBytes,
    SearchReleaseSort Sort);
