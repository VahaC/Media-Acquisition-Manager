using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IDestinationMapper"/>
public sealed class DestinationMapper : IDestinationMapper
{
    public DestinationDto ToDto(DestinationMappingEntity destination, string downloadClientName, DestinationPathCheckResult check) =>
        new(
            destination.Id,
            destination.Name,
            destination.IsDefault,
            destination.DownloadClientConnectionId,
            downloadClientName,
            destination.ClientDestinationKey,
            ToDto(check),
            destination.CreatedUtc);

    public ClientDestinationListResponse ToResponse(ClientDestinationsResult result) =>
        new(
            [.. result.Destinations.Select(listed =>
                new ClientDestinationDto(listed.Destination.Key, listed.Destination.DisplayName, ToDto(listed.Check)))],
            result.Error);

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
