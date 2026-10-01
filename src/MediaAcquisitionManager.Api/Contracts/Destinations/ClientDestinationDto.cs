namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One category a download client offers, as the Add or Edit destination dialog lists it
/// (MAM-11).</summary>
/// <param name="Key">The client's own name for it: what a destination stores and a download sends.</param>
/// <param name="DisplayName">What to show for it; the client's text, cleaned of control characters.</param>
/// <param name="Check">Where it saves and whether that is inside a media library — what a destination of
/// it would show.</param>
public sealed record ClientDestinationDto(string Key, string DisplayName, DestinationCheckDto Check);
