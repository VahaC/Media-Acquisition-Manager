namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — what the app's own look at a replacement's new file found (check 2, ADR 0035).</summary>
public enum NewFileReadabilityStatus
{
    /// <summary>A regular, non-empty media file inside the allowed folders, with a video stream and a running time, the
    /// size the client downloaded, and not one of the old copies.</summary>
    Readable = 1,

    /// <summary>Not yet: the file changed while it was read, or the media inspector couldn't start. Read again on the
    /// next pass, until the files' wait runs out (<see cref="ReplacementVerificationRules.FilesWait"/>).</summary>
    Wait = 2,

    /// <summary>The look can't confirm the file is the new copy, and waiting won't change that: the owner must
    /// look.</summary>
    Review = 3,

    /// <summary>The file is not a usable media file — empty, not a regular file, unreadable as media, or without a video
    /// stream or a running time —, so nothing will be replaced.</summary>
    Failed = 4,
}
