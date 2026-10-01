namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One library of a media library integration, such as the Jellyfin library "Фільми".</summary>
/// <param name="Id">Its id, for the works list's <c>locationId</c> filter.</param>
/// <param name="Name">Its name as the service gives it.</param>
/// <param name="SourceName">The name of the integration that reported it.</param>
public sealed record LibraryLocationDto(Guid Id, string Name, string? SourceName);
