using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ILibraryLinkSuggestionRunSeeder"/>
public sealed class LibraryLinkSuggestionRunSeeder(IServiceProvider services) : SeederBase(services), ILibraryLinkSuggestionRunSeeder
{
    private ILibraryLinkSuggestionRunDataFactory Runs => new LibraryLinkSuggestionRunDataFactory(Db, Resolve<TimeProvider>());

    public Task<LibraryLinkSuggestionRunEntity> Create(
        LibraryLinkSuggestionRunStatus status,
        LibraryLinkSuggestionRunTrigger trigger = LibraryLinkSuggestionRunTrigger.Requested,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        int? worksTotal = null,
        int worksDone = 0,
        Message? error = null) =>
        Runs.Create(status, trigger, requestedBy?.Id, requestedBy?.UserName, createdUtc, worksTotal, worksDone, error);
}
