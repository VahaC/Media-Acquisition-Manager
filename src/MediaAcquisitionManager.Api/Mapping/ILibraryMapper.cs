using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns the library's rows — works with their facts, libraries, integrations, sync runs, and
/// the app's own inspections of the files (MAM-9) — into the contracts of the Library page (MAM-8).</summary>
public interface ILibraryMapper
{
    /// <summary>
    /// A work with what the libraries reported about it: its title and original title each the first of
    /// its kind in title order (<c>LibraryTitleOrder</c>, then ordinal, then id — the order the list is
    /// sorted by), the year given with the chosen title, else with the original one; its identifiers by
    /// scheme, then value; the schemes it lacks; and its versions by source name, edition label, first-seen
    /// time, then id, each with the files <paramref name="filesOf"/> gives.
    /// </summary>
    LibraryWorkDto ToDto(
        MediaWorkEntity work,
        IReadOnlyList<MediaWorkTitleEntity> titles,
        IReadOnlyList<MediaWorkIdentifierEntity> identifiers,
        IReadOnlyList<MediaVersionEntity> versions,
        Func<MediaVersionEntity, IReadOnlyList<LibraryFileDto>> filesOf);

    /// <summary>
    /// Identifier claims the way a work lists them: by scheme, then value, then source name, each claim
    /// (scheme, value, source) once. MAM-12 — also the matched ids of a search's library match.
    /// </summary>
    IReadOnlyList<LibraryIdentifierDto> ToDtos(IReadOnlyList<MediaWorkIdentifierEntity> identifiers);

    /// <summary>
    /// A file with where this app finds it: its service path through <paramref name="pathMappings"/>, the
    /// reporting integration's current ones (<see cref="PathMapper"/>, ADR 0003); none maps it as it is.
    /// <paramref name="locationName"/> is the name of the library that holds it, when one does. Why it has
    /// no app path only when <paramref name="forOwner"/> — for the installation owner, as the reason
    /// quotes the owner's path mappings. MAM-9 — what the app's last look at it found, with the facts of
    /// <paramref name="reading"/> and its <paramref name="tracks"/> (in their order) when the media
    /// inspector read them; the sentence saying why it didn't, again only <paramref name="forOwner"/>.
    /// </summary>
    LibraryFileDto ToDto(
        PhysicalFileEntity file,
        IReadOnlyList<PathMappingPair> pathMappings,
        string? locationName,
        MediaInspectionEntity? reading,
        IReadOnlyList<MediaInspectionTrackEntity> tracks,
        bool forOwner);

    /// <summary>A library as the works filter offers it.</summary>
    LibraryLocationDto ToDto(LibraryLocationEntity location);

    /// <summary>A sync run; its error and who started it only when <paramref name="forOwner"/> — for the
    /// installation owner, as the error can name the service's address and the name is the owner's
    /// sign-in name.</summary>
    LibrarySyncRunDto ToDto(LibrarySyncRunEntity run, bool forOwner);

    /// <summary>A file inspection run (MAM-9); its error and who asked for it only when
    /// <paramref name="forOwner"/>, the sync run's rule.</summary>
    FileInspectionRunDto ToDto(FileInspectionRunEntity run, bool forOwner);

    /// <summary>
    /// A media library integration with where its syncs stand. <paramref name="adapter"/> is the
    /// descriptor of its type, or <c>null</c> when this build no longer registers the type; the caller
    /// works out the rest, which isn't on the row.
    /// </summary>
    LibrarySourceDto ToDto(
        IntegrationConnectionEntity integration,
        IntegrationAdapterDescriptor? adapter,
        bool canReadLibrary,
        LibrarySyncRunDto? latestRun,
        LibrarySyncRunDto? lastSucceededRun,
        int filesWithoutAppPathCount,
        string? pathProblem,
        LibrarySourceInspectionDto inspection);
}
