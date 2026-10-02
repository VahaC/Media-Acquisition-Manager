namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A destination the Download dialog offers (MAM-13).</summary>
/// <param name="Id">The destination's id.</param>
/// <param name="Name">Its name ("Movies").</param>
/// <param name="ClientDestinationKey">The client's category it sends to, exactly as the client spells
/// it.</param>
/// <param name="IsDefault">Whether it is the installation's default destination, which the dialog picks
/// first.</param>
public sealed record DownloadDestinationOptionDto(Guid Id, string Name, string ClientDestinationKey, bool IsDefault);
