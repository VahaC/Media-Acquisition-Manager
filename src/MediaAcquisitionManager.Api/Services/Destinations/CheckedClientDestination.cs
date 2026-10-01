using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>One destination a download client listed, with where it saves and whether that is inside a
/// media library (MAM-11).</summary>
/// <param name="Destination">What the client listed.</param>
/// <param name="Check">Its check against the client's path mappings and the media library folders.</param>
public sealed record CheckedClientDestination(ClientDestination Destination, DestinationPathCheckResult Check);
