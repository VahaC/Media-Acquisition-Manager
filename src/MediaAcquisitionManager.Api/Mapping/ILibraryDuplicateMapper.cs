using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>
/// MAM-20 — turns the groups <see cref="DuplicateGrouping"/> works out, their file names and the works holding them
/// into the contracts of the Duplicates list; a file and a work's ids through <see cref="ILibraryMapper"/>, so they read
/// as the Library page's do.
/// </summary>
public interface ILibraryDuplicateMapper
{
    /// <summary>
    /// A group with its <paramref name="files"/>, already in their order, and the room they take: its class's evidence
    /// filled, the rest empty; the recoverable room only for one file's names and byte-identical files; MAM-21 — the
    /// owner's <paramref name="decision"/> on it, <c>null</c> while undecided.
    /// </summary>
    LibraryDuplicateGroupDto ToDto(
        DuplicateGroup group, IReadOnlyList<LibraryDuplicateFileDto> files, LibrarySpaceDto space, DuplicateDecisionDto? decision);

    /// <summary>
    /// MAM-21 — the owner's decision on a possible match as the list shows it: who gave it only
    /// <paramref name="forOwner"/>, as a link's setter's name.
    /// </summary>
    DuplicateDecisionDto ToDto(DuplicateMatchDecisionEntity decision, bool forOwner);

    /// <summary>
    /// A file name of a group: <paramref name="file"/> as the Library page shows it
    /// (<see cref="ILibraryMapper.ToDto(PhysicalFileEntity, IReadOnlyList{PathMappingPair}, string?, MediaInspectionEntity?, IReadOnlyList{MediaInspectionTrackEntity}, bool)"/>,
    /// the owner-only sentences only <paramref name="forOwner"/>), what the app read of it from
    /// <paramref name="name"/>, and the <paramref name="works"/> holding it in title order, then by id.
    /// </summary>
    LibraryDuplicateFileDto ToDto(
        DuplicateName name,
        PhysicalFileEntity file,
        IReadOnlyList<PathMappingPair> pathMappings,
        string? locationName,
        MediaInspectionEntity? reading,
        IReadOnlyList<MediaInspectionTrackEntity> tracks,
        IReadOnlyList<LibraryDuplicateWorkDto> works,
        bool forOwner);

    /// <summary>
    /// A work holding a file through <paramref name="version"/>: the title and year its Library card shows, an
    /// episode's place as its card shows it with the title of that place's series (<paramref name="seriesTitlesOf"/>
    /// gives a series' titles), and its ids as <paramref name="linked"/> counts them.
    /// </summary>
    LibraryDuplicateWorkDto ToDto(
        MediaWorkEntity work,
        IReadOnlyList<MediaWorkTitleEntity> titles,
        IReadOnlyList<MediaEpisodePositionEntity> positions,
        Func<Guid, IReadOnlyList<MediaWorkTitleEntity>> seriesTitlesOf,
        MediaVersionEntity version,
        LinkedWork linked);
}
