using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns destinations, what a download client offers, and their checks into the contracts of the
/// Destinations settings page (MAM-11).</summary>
public interface IDestinationMapper
{
    /// <summary>
    /// A destination as the page shows it, with the name of its download client
    /// (<paramref name="downloadClientName"/>) and its <paramref name="check"/>, worked out by the caller.
    /// </summary>
    DestinationDto ToDto(DestinationMappingEntity destination, string downloadClientName, DestinationPathCheckResult check);

    /// <summary>What a download client offers, each category with its check, in the client's order — or
    /// why it couldn't be asked.</summary>
    ClientDestinationListResponse ToResponse(ClientDestinationsResult result);

    /// <summary>
    /// MAM-32 — a release source with its <paramref name="sections"/>, each naming its destination from
    /// <paramref name="destinationNames"/>, and the sentence saying why it couldn't be asked
    /// (<paramref name="error"/>), if it couldn't.
    /// </summary>
    DestinationSectionSourceDto ToDto(
        IntegrationConnectionEntity source,
        IReadOnlyList<DestinationSectionGroup> sections,
        IReadOnlyDictionary<Guid, string> destinationNames,
        string? error);
}
