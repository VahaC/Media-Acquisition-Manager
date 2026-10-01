using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ISearchDataFactory"/>
/// <remarks>A search's sources are its own rows, so they are added through the search's factory rather
/// than a factory of their own.</remarks>
public sealed class SearchDataFactory(ApplicationDbContext db, TimeProvider time) : ISearchDataFactory
{
    public async Task<SearchEntity> Create(Guid userId, string text = "Interstellar", int? year = null, string? imdbId = null, string? tmdbId = null)
    {
        var search = new SearchEntity
        {
            UserId = userId,
            Text = text,
            Year = year,
            ImdbId = imdbId,
            TmdbId = tmdbId,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.Searches.Add(search);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return search;
    }

    public async Task<SearchSourceEntity> AddSource(
        Guid searchId,
        Guid? integrationConnectionId,
        string name,
        int position = 0,
        IntegrationCallStatus? status = IntegrationCallStatus.Succeeded,
        ReleaseSourceNotSearchedReason? notSearchedReason = null,
        string? error = null)
    {
        var source = new SearchSourceEntity
        {
            SearchId = searchId,
            IntegrationConnectionId = integrationConnectionId,
            Name = name,
            Position = position,
            Status = status,
            NotSearchedReason = notSearchedReason,
            Error = error,
        };

        db.SearchSources.Add(source);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return source;
    }
}
