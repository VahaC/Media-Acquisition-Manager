namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// What the media inspector read from a file (MAM-9): verified facts, unlike a release's claimed
/// properties (specification §3.1). A value the file didn't give is <c>null</c>, which the page shows
/// as unknown.
/// </summary>
/// <param name="DurationMilliseconds">How long it plays.</param>
/// <param name="BitRate">The overall bit rate in bits per second.</param>
/// <param name="Tracks">Its video, audio and subtitle tracks in the file's own order; cover art and
/// attachments left out.</param>
public sealed record MediaFactsDto(long? DurationMilliseconds, long? BitRate, IReadOnlyList<MediaTrackDto> Tracks);
