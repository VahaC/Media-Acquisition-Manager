namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// What the media inspector read from a file's container and streams (MAM-9): verified by opening the
/// file, unlike a release's claimed properties (specification §3.1). A value the file didn't give is
/// <c>null</c>, never a guess.
/// </summary>
/// <param name="Duration">How long it plays; <c>null</c> when the container doesn't say.</param>
/// <param name="BitRate">The overall bit rate in bits per second; <c>null</c> when the container
/// doesn't say.</param>
/// <param name="Tracks">Its video, audio and subtitle tracks in the file's own order; cover art and
/// attachments left out.</param>
public sealed record MediaFacts(TimeSpan? Duration, long? BitRate, IReadOnlyList<MediaTrackFacts> Tracks);
