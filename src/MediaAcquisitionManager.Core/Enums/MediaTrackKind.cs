namespace MediaAcquisitionManager.Core.Enums;

/// <summary>What one track of a media file carries (MAM-9). Stored by name.</summary>
/// <remarks>
/// Cover art, which a container lists as a video stream marked as an attached picture, is not a track
/// here; neither are fonts and other attachments.
/// </remarks>
public enum MediaTrackKind
{
    /// <summary>A picture track.</summary>
    Video = 1,

    /// <summary>A sound track.</summary>
    Audio = 2,

    /// <summary>A subtitle track, text or picture based.</summary>
    Subtitle = 3,
}
