using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IDestinationMapper"/>
/// <remarks>MAM-47 (ADR 0042) — renders the clients', the sources' and the checks' sentences in the request's language
/// (<see cref="IRequestTexts"/>).</remarks>
public sealed class DestinationMapper(IRequestTexts texts) : IDestinationMapper
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
            texts.Render(result.Error),
            destinationKind,
            // MAM-30 — the library folders a folder client has no folder for, each with how to fix it (ADR 0039); never
            // null for the page.
            [.. result.Unplaced.Select(folder => new UnplacedLibraryFolderDto(folder.LibraryName, folder.Folder, texts.Render(folder.Problem)))]);

    public DestinationSectionSourceDto ToDto(
        IntegrationConnectionEntity source,
        IReadOnlyList<DestinationSectionGroup> sections,
        IReadOnlyDictionary<Guid, string> destinationNames,
        Message? error) =>
        new(
            source.Id,
            source.Name,
            [.. sections.Select(section => new DestinationSectionDto(
                section.Name,
                section.CategoryIds,
                section.IsSourceSpecific,
                section.DestinationId,
                section.DestinationId is { } destinationId ? destinationNames.GetValueOrDefault(destinationId) : null))],
            texts.Render(error));

    private DestinationCheckDto ToDto(DestinationPathCheckResult check) =>
        new(check.Status, check.ServicePath, check.AppPath, check.LibraryNames, texts.Render(check.Problem));
}
