using MediaAcquisitionManager.Api.Contracts;
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
    /// registers the type.
    /// </summary>
    IntegrationDto ToDto(IntegrationConnectionEntity connection, IntegrationAdapterDescriptor? adapter);

    /// <summary>A type of service as the Add integration dialog offers it.</summary>
    IntegrationAdapterTypeDto ToDto(IntegrationAdapterDescriptor adapter);

    /// <summary>A connection test's outcome, its message already masked.</summary>
    ConnectionTestResponse ToResponse(ConnectionTestResult result);
}
