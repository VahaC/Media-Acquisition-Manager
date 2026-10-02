namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What one release can be sent to: the Download dialog's choices (MAM-13).</summary>
/// <remarks>
/// MAM-13 — names only, for every signed-in user (decision 1): no address, folder, path mapping or
/// client's sentence, which stay the owner's on the Integrations and Destinations pages.
/// </remarks>
/// <param name="Clients">Every switched-on download client of a type this build can send to that takes
/// the release's payload and has a destination, by name.</param>
public sealed record DownloadOptionsResponse(IReadOnlyList<DownloadClientOptionDto> Clients);
