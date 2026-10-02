namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A download client the Download dialog offers (MAM-13).</summary>
/// <param name="Id">The client integration's id.</param>
/// <param name="Name">Its name.</param>
/// <param name="TypeName">What it is ("qBittorrent").</param>
/// <param name="Destinations">Its destinations, by name; never empty.</param>
public sealed record DownloadClientOptionDto(Guid Id, string Name, string TypeName, IReadOnlyList<DownloadDestinationOptionDto> Destinations);
