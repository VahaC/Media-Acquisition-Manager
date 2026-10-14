using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IDestinationMapper"/>
public sealed class DestinationMapper : IDestinationMapper
{
    public DestinationDto ToDto(
        DestinationMappingEntity destination, string downloadClientName, DestinationPathCheckResult check, DownloadDestinationKind? destinationKind) =>
        new(
            destination.Id,
            destination.Name,
            destination.IsDefault,
            destination.DownloadClientConnectionId,
            downloadClientName,
            destination.ClientDestinationKey,
            ToDto(check),
            destination.CreatedUtc,
            destinationKind);

    public ClientDestinationListResponse ToResponse(ClientDestinationsResult result, DownloadDestinationKind? destinationKind) =>
        new(
            [.. result.Destinations.Select(listed => new ClientDestinationDto(
                listed.Destination.Key, listed.Destination.DisplayName, ToDto(listed.Check), listed.Destination.IsClientDefaultFolder))],
            result.Error,
            destinationKind,
            // MAM-30 — the library folders a folder client has no folder for, each with how to fix it (ADR 0039); never
            // null for the page.
            [.. result.Unplaced.Select(folder => new UnplacedLibraryFolderDto(folder.LibraryName, folder.Folder, folder.Problem))]);

    public DestinationSectionSourceDto ToDto(
        IntegrationConnectionEntity source,
        IReadOnlyList<DestinationSectionGroup> sections,
        IReadOnlyDictionary<Guid, string> destinationNames,
        string? error) =>
        new(
            source.Id,
            source.Name,
            [.. sections.Select(section => new DestinationSectionDto(
                section.Name,
                section.CategoryIds,
                section.IsSourceSpecific,
                section.DestinationId,
                section.DestinationId is { } destinationId ? destinationNames.GetValueOrDefault(destinationId) : null))],
            error);

    private static DestinationCheckDto ToDto(DestinationPathCheckResult check) =>
        new(check.Status, check.ServicePath, check.AppPath, check.LibraryNames, check.Problem);
}
