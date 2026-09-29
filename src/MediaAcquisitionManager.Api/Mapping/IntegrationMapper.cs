using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IIntegrationMapper"/>
public sealed class IntegrationMapper : IIntegrationMapper
{
    public IntegrationDto ToDto(IntegrationConnectionEntity connection, IntegrationAdapterDescriptor? adapter) =>
        new(
            connection.Id,
            connection.Name,
            connection.AdapterType,
            adapter?.DisplayName,
            connection.Kind,
            connection.IsEnabled,
            connection.BaseUrl,
            connection.UserName,
            // MAM-7 — presence only (ADR 0007): the sealed value never leaves, not even to be
            // counted or compared.
            HasPassword: connection.PasswordEncrypted is not null,
            HasApiKey: connection.ApiKeyEncrypted is not null,
            [.. connection.PathMappings
                .OrderBy(mapping => mapping.Position)
                .Select(mapping => new PathMappingDto(mapping.ServicePrefix, mapping.AppPrefix))],
            connection.CreatedUtc);

    public IntegrationAdapterTypeDto ToDto(IntegrationAdapterDescriptor adapter) =>
        new(
            adapter.TypeId,
            adapter.DisplayName,
            adapter.Kind,
            adapter.UserName,
            adapter.Password,
            adapter.ApiKey,
            adapter.ReportsFilePaths);

    public ConnectionTestResponse ToResponse(ConnectionTestResult result) => new(result.Status, result.Message);
}
