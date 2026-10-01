namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>What one read of a media file found (MAM-9).</summary>
/// <param name="File">What the file system said about the file, the same before and after the
/// read: the key the streams are kept under.</param>
/// <param name="Media">What the media inspector read from the file.</param>
public sealed record MediaInspection(FileFacts File, MediaFacts Media);
