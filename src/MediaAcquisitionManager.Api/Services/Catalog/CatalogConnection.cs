using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;

namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>
/// MAM-33 — the metadata catalog a computation reads from, ready to call: the integration, its settings with
/// the secrets opened, and its adapter behind the two catalog contracts (Strategy, ADR 0005). Built for one
/// run or pass and never stored; safe to print, as the settings mask their own secrets.
/// </summary>
/// <param name="IntegrationId">The metadata integration: every catalog row it reports carries it.</param>
/// <param name="Name">Its name, copied onto those rows (the audit-row pattern).</param>
/// <param name="Settings">What every call needs, secrets decrypted.</param>
/// <param name="Adapter">The adapter, for its connection test.</param>
/// <param name="Catalog">The adapter as the catalog of works, collections, people and genres.</param>
/// <param name="Resolver">The adapter as the lookup of a work by an id or a title.</param>
public sealed record CatalogConnection(
    Guid IntegrationId,
    string Name,
    IntegrationConnectionSettings Settings,
    IIntegrationAdapter Adapter,
    IMetadataCatalog Catalog,
    IMetadataResolver Resolver);
