using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IReleaseSeeder"/>
public sealed class ReleaseSeeder(IServiceProvider services) : SeederBase(services), IReleaseSeeder
{
    /// <summary>The <c>link=</c> token of the default download link, a credential of Prowlarr's own (ADR 0004)
    /// that no answer, log line or job may repeat.</summary>
    public const string PlantedLinkToken = "planted-link-token-0001";

    private IReleaseDataFactory Releases => new ReleaseDataFactory(Db, Resolve<IEncryptionService>(), Resolve<TimeProvider>());

    private ISearchDataFactory Searches => new SearchDataFactory(Db, Resolve<TimeProvider>());

    public Task<ReleaseEntity> Create(
        IntegrationConnectionEntity source,
        string? title = null,
        ReleaseKind kind = ReleaseKind.Movie,
        string? description = null,
        string? tmdbId = null,
        string? categoryIds = null) =>
        title is null
            ? Releases.Create(source.Id, source.Name, categoryIds: categoryIds, kind: kind, description: description, tmdbId: tmdbId)
            : Releases.Create(source.Id, source.Name, title, categoryIds: categoryIds, kind: kind, description: description, tmdbId: tmdbId);

    public async Task<ReleaseEntity> CreateFound(
        UserEntity user,
        IntegrationConnectionEntity source,
        string title = "Interstellar (2014) BDRip 1080p",
        string? locator = null,
        string? sourceItemId = null,
        string? infoHash = null,
        ReleasePayloadKind? payloadKind = null,
        string? categoryIds = null)
    {
        var search = await Searches.Create(user.Id);
        var searchSource = await Searches.AddSource(search.Id, source.Id, source.Name);
        // Stored as the adapter stores it: the key replaced by its placeholder, put back at the fetch.
        var link = locator
            ?? $"{TorznabSamples.DownloadBaseFor(source.BaseUrl)}download?apikey={TorznabLinks.ApiKeyPlaceholder}&link={PlantedLinkToken}";
        return await Releases.Create(
            source.Id,
            source.Name,
            title,
            link,
            searchSource.Id,
            // A listing's own id, so two found releases are two listings unless a test says otherwise.
            sourceItemId ?? $"https://toloka.to/t{Guid.NewGuid():N}",
            infoHash,
            payloadKind,
            categoryIds);
    }
}
