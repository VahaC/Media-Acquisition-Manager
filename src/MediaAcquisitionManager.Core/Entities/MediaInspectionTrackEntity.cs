using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One track of a media inspection (MAM-9): a video, audio or subtitle stream as the media inspector
/// read it. A value that doesn't apply to its kind, or that the stream didn't give, is <c>null</c>.
/// </summary>
/// <remarks>
/// No <c>CreatedUtc</c>: a track is part of its inspection, written and removed with it.
/// </remarks>
public class MediaInspectionTrackEntity : BaseEntity
{
    /// <summary>
    /// The longest <see cref="Codec"/>, <see cref="Profile"/> and <see cref="ChannelLayout"/> the columns
    /// hold. The inspector's names are short tokens (<c>hevc</c>, <c>DTS-HD MA</c>, <c>5.1(side)</c>), so
    /// a longer value is not one of them and is stored as unknown rather than cut.
    /// </summary>
    public const int NameMaximumLength = 64;

    /// <summary>
    /// The longest <see cref="Language"/> the column holds: a BCP 47 tag's practical maximum. A longer
    /// tag is not a language and is stored as unknown.
    /// </summary>
    public const int LanguageMaximumLength = 35;

    /// <summary>The inspection; the track is removed with it.</summary>
    public Guid MediaInspectionId { get; set; }

    /// <summary>The 0-based position among the inspection's tracks, in the file's own order.</summary>
    public int Position { get; set; }

    /// <summary>What the track carries.</summary>
    public MediaTrackKind Kind { get; set; }

    /// <summary>The codec as the inspector names it (<c>hevc</c>, <c>ac3</c>, <c>subrip</c>).</summary>
    [MaxLength(NameMaximumLength)]
    public string? Codec { get; set; }

    /// <summary>The codec profile as the inspector names it (<c>Main 10</c>, <c>DTS-HD MA</c>).</summary>
    [MaxLength(NameMaximumLength)]
    public string? Profile { get; set; }

    /// <summary>A video track's width in pixels.</summary>
    public int? Width { get; set; }

    /// <summary>A video track's height in pixels.</summary>
    public int? Height { get; set; }

    /// <summary>A video track's dynamic range.</summary>
    public DynamicRange? DynamicRange { get; set; }

    /// <summary>An audio track's channel count.</summary>
    public int? Channels { get; set; }

    /// <summary>An audio track's channel layout as the inspector names it (<c>5.1(side)</c>).</summary>
    [MaxLength(NameMaximumLength)]
    public string? ChannelLayout { get; set; }

    /// <summary>
    /// The language the track is tagged with, lower case (<c>ukr</c>, <c>eng</c>); <c>null</c> when it
    /// is untagged or tagged undetermined (<c>und</c>). A tag is what the file claims; the inspector
    /// doesn't listen to the track.
    /// </summary>
    [MaxLength(LanguageMaximumLength)]
    public string? Language { get; set; }

    /// <summary>The file marks it as the track to play by default.</summary>
    public bool IsDefault { get; set; }

    /// <summary>The file marks it as forced (subtitles shown even when subtitles are off).</summary>
    public bool IsForced { get; set; }

    /// <summary>The file marks it as meant for the hearing impaired (SDH).</summary>
    public bool IsHearingImpaired { get; set; }
}
