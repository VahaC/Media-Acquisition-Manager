using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ISourceFeedEntrySeeder"/>
public sealed class SourceFeedEntrySeeder(IServiceProvider services) : SeederBase(services), ISourceFeedEntrySeeder
{
    private ISourceFeedEntryDataFactory Entries => new SourceFeedEntryDataFactory(Db, Resolve<TimeProvider>());

    public Task<SourceFeedEntryEntity> Create(
        IntegrationConnectionEntity source,
        string title,
        CatalogWorkEntity? resolvedTo,
        DateTime? seenUtc = null,
        DateTime? publishedUtc = null,
        ReleaseKind kind = ReleaseKind.Movie,
        string? sourceItemId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var seen = seenUtc ?? Resolve<TimeProvider>().GetUtcNow().UtcDateTime;
        // A Torznab guid's shape; unique per entry, as a source's are.
        var guid = sourceItemId ?? $"https://tracker.example/t{Guid.NewGuid():N}";
        return Entries.Create(
            source.Id, source.Name, guid, title, kind, seen, publishedUtc, resolvedTo is null ? null : seen, resolvedTo?.Id);
    }
}
