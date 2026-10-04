namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What one release can be sent to: the Download dialog's choices (MAM-13).</summary>
/// <remarks>
/// MAM-13 — names only, for every signed-in user (decision 1): no address, folder, path mapping or
/// client's sentence, which stay the owner's on the Integrations and Destinations pages.
/// </remarks>
/// <param name="Clients">Every switched-on download client of a type this build can send to that takes
/// the release's payload and has a destination, by name.</param>
/// <param name="SuggestedDestinationId">MAM-32 — the destination the dialog picks first, always one of
/// <paramref name="Clients"/>': the one the release's tracker section is mapped to, the source's own
/// section winning over a standard category; the installation's default when no section of it is mapped to
/// one offered here; <c>null</c> when two destinations tie at the rank that decides, or there is no default
/// to fall back to (ADR 0020).</param>
public sealed record DownloadOptionsResponse(IReadOnlyList<DownloadClientOptionDto> Clients, Guid? SuggestedDestinationId);
