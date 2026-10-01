using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// The quality and languages a release's title claims (MAM-10): claims, unlike a file's
/// <see cref="MediaFactsDto"/>, which the app read itself (specification §3.1). A value the title doesn't
/// state is <c>null</c> or empty, which the page shows as unknown.
/// </summary>
/// <param name="Resolution">The resolution class the title names (2160, 1080, 720, 576, 480, or a frame
/// size's height) — a class, not the picture's height in pixels.</param>
/// <param name="Medium">What the title says it was made from.</param>
/// <param name="VideoCodec">The video codec in the media inspector's names (<c>hevc</c>,
/// <c>h264</c>…), so it compares with a file's track.</param>
/// <param name="DynamicRanges">Every dynamic range the title names; empty when it names none.</param>
/// <param name="AudioLanguages">Audio languages as lower-case ISO 639-2 codes, as tracks are tagged
/// (<c>ukr</c>, <c>eng</c>; <c>mul</c> for "Multi").</param>
/// <param name="SubtitleLanguages">Subtitle languages the same way.</param>
public sealed record ClaimedReleaseQualityDto(
    int? Resolution,
    ReleaseMedium? Medium,
    string? VideoCodec,
    IReadOnlyList<DynamicRange> DynamicRanges,
    IReadOnlyList<string> AudioLanguages,
    IReadOnlyList<string> SubtitleLanguages);
