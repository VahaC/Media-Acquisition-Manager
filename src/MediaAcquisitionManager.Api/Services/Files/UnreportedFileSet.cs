using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// MAM-44 — the files in no library (<see cref="UnreportedFileReader"/>), each with its place among the libraries'
/// folders, sorted the one way every reader counts them: ignored by the owner, else taken by the library for a film's
/// extra, else counted. The Media folders row, the bar and the Files in no library list read this one set, so their
/// counts can't disagree.
/// </summary>
/// <remarks>
/// An extra — in an extras folder or named as one (<see cref="UnreportedFilePlaceKind.ExtrasFolder"/>,
/// <see cref="UnreportedFilePlaceKind.ExtraName"/>) — warns of nothing, not even when the app couldn't read it
/// (Vasyl's decisions of 2026-10-13): Jellyfin skips it by its own rule, so it isn't a file the owner has to settle.
/// It still takes its room, which every reader counts over <see cref="All"/>. The owner's mark comes first: an ignored
/// extra is listed with the ignored files.
/// </remarks>
public sealed class UnreportedFileSet
{
    private readonly IReadOnlyDictionary<Guid, UnreportedFilePlaceDto> _places;

    public UnreportedFileSet(List<PhysicalFileEntity> all, IReadOnlyDictionary<Guid, UnreportedFilePlaceDto> places)
    {
        All = all;
        _places = places;
        Ignored = [.. all.Where(file => file.IgnoredUtc is not null)];
        Extras = [.. all.Where(file => file.IgnoredUtc is null && IsExtra(places[file.Id]))];
        Counted = [.. all.Where(file => file.IgnoredUtc is null && !IsExtra(places[file.Id]))];
    }

    /// <summary>Every file in no library, in path order (ordinal), then id: what the room is counted over.</summary>
    public IReadOnlyList<PhysicalFileEntity> All { get; }

    /// <summary>The files the app counts and warns of: neither ignored nor an extra.</summary>
    public IReadOnlyList<PhysicalFileEntity> Counted { get; }

    /// <summary>The files the library takes for a film's extras, not ignored: listed apart, warning of nothing.</summary>
    public IReadOnlyList<PhysicalFileEntity> Extras { get; }

    /// <summary>The files the owner told the app to stop counting, extras included.</summary>
    public IReadOnlyList<PhysicalFileEntity> Ignored { get; }

    /// <summary>Where <paramref name="file"/>, one of <see cref="All"/>, sits among the libraries' folders.</summary>
    public UnreportedFilePlaceDto PlaceOf(PhysicalFileEntity file) => _places[file.Id];

    private static bool IsExtra(UnreportedFilePlaceDto place) =>
        place.Kind is UnreportedFilePlaceKind.ExtrasFolder or UnreportedFilePlaceKind.ExtraName;
}
