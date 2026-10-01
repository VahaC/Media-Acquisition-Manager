using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// The quality and languages a release's title claims (specification §3.1: "quality inferred from a
/// title is marked as claimed, not verified by inspecting the file"). A value the title doesn't state
/// is <c>null</c> or empty: unknown, never assumed.
/// </summary>
/// <remarks>
/// MAM-10 — named in the media inspector's own terms where they overlap (MAM-9), so MAM-12 can set a
/// verified file beside a claimed release without translating one vocabulary into the other.
/// </remarks>
/// <param name="Resolution">The resolution class the title names: 2160 for 2160p, 4K or UHD, 1080, 720,
/// 576, 480, or the height of a frame size (360 for 640x360). A class, not a pixel height: a scope
/// 1080p film is 1920x800 once read.</param>
/// <param name="Medium">What the release was made from.</param>
/// <param name="VideoCodec">The video codec as the media inspector names it: <c>hevc</c>, <c>h264</c>,
/// <c>av1</c>, <c>vc1</c>, <c>mpeg4</c> (XviD, DivX) or <c>mpeg2video</c>.</param>
/// <param name="DynamicRanges">Every dynamic range the title names; a hybrid release names Dolby Vision
/// and HDR10 both. Empty when it names none — not assumed SDR.</param>
/// <param name="AudioLanguages">The audio languages named, as lower-case ISO 639-2 codes like a track's
/// tag (<c>ukr</c>, <c>eng</c>; <c>mul</c> for "Multi"), in the title's order.</param>
/// <param name="SubtitleLanguages">The subtitle languages named after "Sub", the same way.</param>
public sealed record ClaimedReleaseQuality(
    int? Resolution,
    ReleaseMedium? Medium,
    string? VideoCodec,
    IReadOnlyList<DynamicRange> DynamicRanges,
    IReadOnlyList<string> AudioLanguages,
    IReadOnlyList<string> SubtitleLanguages);
