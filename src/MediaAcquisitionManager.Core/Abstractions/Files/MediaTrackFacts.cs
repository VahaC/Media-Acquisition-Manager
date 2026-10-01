using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// One track of a media file as the media inspector read it (MAM-9). A value that doesn't apply to the
/// track's kind, or that the stream didn't give, is <c>null</c>.
/// </summary>
/// <param name="Kind">What the track carries.</param>
/// <param name="Codec">The codec as the inspector names it (<c>hevc</c>, <c>ac3</c>, <c>subrip</c>).</param>
/// <param name="Profile">The codec profile as the inspector names it (<c>Main 10</c>,
/// <c>DTS-HD MA</c>).</param>
/// <param name="Width">A video track's width in pixels.</param>
/// <param name="Height">A video track's height in pixels.</param>
/// <param name="DynamicRange">A video track's dynamic range; <c>null</c> when the stream doesn't
/// say.</param>
/// <param name="Channels">An audio track's channel count.</param>
/// <param name="ChannelLayout">An audio track's channel layout as the inspector names it
/// (<c>5.1(side)</c>).</param>
/// <param name="Language">The language the track is tagged with, lower case (<c>ukr</c>); <c>null</c>
/// when untagged or tagged undetermined.</param>
/// <param name="IsDefault">The file marks it as the track to play by default.</param>
/// <param name="IsForced">The file marks it as forced.</param>
/// <param name="IsHearingImpaired">The file marks it as meant for the hearing impaired.</param>
public sealed record MediaTrackFacts(
    MediaTrackKind Kind,
    string? Codec,
    string? Profile,
    int? Width,
    int? Height,
    DynamicRange? DynamicRange,
    int? Channels,
    string? ChannelLayout,
    string? Language,
    bool IsDefault,
    bool IsForced,
    bool IsHearingImpaired);
