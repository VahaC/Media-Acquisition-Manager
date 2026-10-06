using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
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
    /// scheme, then value; the schemes of its kind it lacks; and its versions by source name, edition label,
    /// first-seen time, then id, each with the files <paramref name="filesOf"/> gives. MAM-32 — an episode
    /// with the first of its <paramref name="positions"/> by season (the regular ones, then the specials,
    /// then none), then number; a series with its
    /// <paramref name="seasons"/>.
    /// </summary>
    /// <remarks>
    /// MAM-34 — its ids are <paramref name="linked"/>'s, its items' TMDb links applied (<c>ILibraryLinkReader</c>), with
    /// the links as one line: what they link to, the worst of their states, a conflicting library id, and — only
    /// <paramref name="forOwner"/> — who set them and why the library doesn't hold them. A work that counts as linked
    /// lacks no id (decision 9, R16): the one rule the missing-ids filter applies too.
    /// </remarks>
    LibraryWorkDto ToDto(
        MediaWorkEntity work,
        IReadOnlyList<MediaWorkTitleEntity> titles,
        LinkedWork linked,
        IReadOnlyList<MediaVersionEntity> versions,
        Func<MediaVersionEntity, IReadOnlyList<LibraryFileDto>> filesOf,
        IReadOnlyList<MediaEpisodePositionEntity> positions,
        IReadOnlyList<LibrarySeasonDto>? seasons,
        bool forOwner);

    /// <summary>
    /// Identifier claims the way a work lists them: by scheme, then value, then source name, each claim
    /// (scheme, value, source) once. MAM-12 — also the matched ids of a search's library match. MAM-34 — with the kind of
    /// source that claimed it, and when a person set it.
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

    /// <summary>A file inspection run (MAM-9); its error, who asked for it and (MAM-19) its first hashing
    /// problem only when <paramref name="forOwner"/>, the sync run's rule — the problem quotes the owner's
    /// paths.</summary>
    FileInspectionRunDto ToDto(FileInspectionRunEntity run, bool forOwner);

    /// <summary>A scan of the media folders (MAM-18); who asked for it, its first problem and its error
    /// only when <paramref name="forOwner"/>, the inspection run's rule — the problem quotes the owner's
    /// paths.</summary>
    FolderScanRunDto ToDto(FolderScanRunEntity run, bool forOwner);

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
        LibrarySourceInspectionDto inspection,
        LibrarySpaceDto space);
}
