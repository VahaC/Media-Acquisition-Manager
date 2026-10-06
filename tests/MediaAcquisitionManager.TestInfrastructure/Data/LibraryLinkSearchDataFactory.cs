using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ILibraryLinkSearchDataFactory"/>
/// <remarks>The candidates are the search's own rows, so they are added through the search's factory rather than a
/// factory of their own.</remarks>
public sealed class LibraryLinkSearchDataFactory(ApplicationDbContext db, TimeProvider time) : ILibraryLinkSearchDataFactory
{
    public async Task<LibraryLinkSearchEntity> Create(
        Guid mediaWorkId,
        string queryKey,
        IReadOnlyList<(MediaWorkKind Kind, string CatalogId, LibraryLinkCandidateSource Source)> candidates,
        DateTime? searchedUtc = null,
        DateTime? noneFitsUtc = null,
        Guid? noneFitsByUserId = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var search = new LibraryLinkSearchEntity
        {
            MediaWorkId = mediaWorkId,
            QueryKey = queryKey,
            SearchedUtc = searchedUtc ?? now,
            NoneFitsUtc = noneFitsUtc,
            NoneFitsByUserId = noneFitsByUserId,
            CreatedUtc = now,
        };

        db.LibraryLinkSearches.Add(search);
        db.LibraryLinkCandidates.AddRange(candidates.Select((candidate, position) => new LibraryLinkCandidateEntity
        {
            LibraryLinkSearchId = search.Id,
            Position = position,
            CatalogKind = candidate.Kind,
            CatalogId = candidate.CatalogId,
            Source = candidate.Source,
        }));
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return search;
    }
}
