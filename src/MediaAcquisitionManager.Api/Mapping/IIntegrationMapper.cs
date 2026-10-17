using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns integration rows, adapter types and connection tests into the contracts that
/// describe them.</summary>
public interface IIntegrationMapper
{
    /// <summary>
    /// The integration as the settings page shows it: its path mappings in the order entered, and
    /// whether each secret is stored — read from the column being set, never from what it holds.
    /// <paramref name="adapter"/> is the descriptor of its type, or <c>null</c> when this build no longer
    /// registers the type. MAM-30 — <paramref name="destinationKind"/> is what a download client type's destinations
    /// are, asked of its capabilities by the caller; <c>null</c> for any other type. MAM-46 — a metadata catalog's
    /// language is the row's own.
    /// </summary>
    IntegrationDto ToDto(IntegrationConnectionEntity connection, IntegrationAdapterDescriptor? adapter, DownloadDestinationKind? destinationKind);

    /// <summary>A type of service as the Add integration dialog offers it, with — MAM-30 — what its destinations are
    /// when it is a download client (<paramref name="destinationKind"/>) and — MAM-46 — the languages it can be asked in
    /// when it is a metadata catalog (<paramref name="catalogLanguages"/>).</summary>
    IntegrationAdapterTypeDto ToDto(IntegrationAdapterDescriptor adapter, DownloadDestinationKind? destinationKind, IReadOnlyList<string>? catalogLanguages);

    /// <summary>A connection test's outcome, its message already masked.</summary>
    ConnectionTestResponse ToResponse(ConnectionTestResult result);
}
