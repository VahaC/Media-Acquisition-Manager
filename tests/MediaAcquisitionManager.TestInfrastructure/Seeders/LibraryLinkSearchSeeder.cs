using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ILibraryLinkSearchSeeder"/>
public sealed class LibraryLinkSearchSeeder(IServiceProvider services) : SeederBase(services), ILibraryLinkSearchSeeder
{
    private ILibraryLinkSearchDataFactory Searches => new LibraryLinkSearchDataFactory(Db, Resolve<TimeProvider>());

    public Task<LibraryLinkSearchEntity> Create(
        MediaWorkEntity work,
        string queryKey,
        IReadOnlyList<(MediaWorkKind Kind, string CatalogId, LibraryLinkCandidateSource Source)> candidates,
        DateTime? searchedUtc = null,
        DateTime? noneFitsUtc = null,
        UserEntity? noneFitsBy = null) =>
        Searches.Create(work.Id, queryKey, candidates, searchedUtc, noneFitsUtc, noneFitsBy?.Id);
}
