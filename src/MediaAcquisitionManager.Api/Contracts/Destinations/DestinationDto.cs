using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A destination as the Destinations settings page shows it (MAM-11).</summary>
/// <remarks>
/// MAM-11 — no folder of the app's own: a destination is the download client's own category, and the
/// only thing a download ever sends is <paramref name="ClientDestinationKey"/> (specification §3.2). Where
/// that category saves is the client's to say, read live into <paramref name="Check"/>.
/// </remarks>
/// <param name="Id">The destination's id.</param>
/// <param name="Name">Its name ("Movies", "Movies 4K"), unique per download client in any letter case.</param>
/// <param name="IsDefault">Whether it is the installation's default destination; at most one is.</param>
/// <param name="DownloadClientId">The download client integration it belongs to.</param>
/// <param name="DownloadClientName">That integration's name.</param>
/// <param name="ClientDestinationKey">The client's own name for the category, exactly as the client
/// spells it.</param>
/// <param name="Check">Where the category saves and whether that is inside a media library, as the
/// client answered when this was read.</param>
/// <param name="CreatedUtc">When it was added.</param>
/// <param name="DestinationKind">MAM-30 — what <paramref name="ClientDestinationKey"/> is, by its client's type: a
/// category, or a folder as the client sees it (ADR 0039); <c>null</c> when this version of the app no longer connects
/// to that type.</param>
public sealed record DestinationDto(
    Guid Id,
    string Name,
    bool IsDefault,
    Guid DownloadClientId,
    string DownloadClientName,
    string ClientDestinationKey,
    DestinationCheckDto Check,
    DateTime CreatedUtc,
    DownloadDestinationKind? DestinationKind = null);
