namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-26 — what one name a replacement moves into the quarantine is (ADR 0030 "What"): a ticked copy's video, or one of
/// its own companion files beside it. Stored by name.
/// </summary>
public enum ReplacementNameKind
{
    /// <summary>The ticked copy's video file itself: one that can't move stops the job.</summary>
    Video = 1,

    /// <summary>A file named after the video, beside it, that is no media file itself (<c>Alien.uk.srt</c>,
    /// <c>Alien.nfo</c>): one that can't move stays where it is, named on the job, and the video still moves.</summary>
    Companion = 2,
}
