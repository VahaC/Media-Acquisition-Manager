using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-27 — one new video file a replacement's download brought, as the job's page lists it (ADR 0035): what each check
/// found of it at its last run — the client's word, the disk's and the media inspector's, the item the library made of it
/// and its ids, the work they identified it as or why it stopped the job — and how far the check got with it.
/// </summary>
/// <param name="Id">The new file's row: what an old copy's pair names.</param>
/// <param name="Position">Its 0-based place among the download's files, in the client's own order.</param>
/// <param name="RelativePath">Its path relative to the download's folder, as the client names it.</param>
/// <param name="AppPath">Its path inside the app; <c>null</c> when the client's path mappings give none.</param>
/// <param name="ClientSizeBytes">Check 1 — its size, as the client reported it; <c>null</c> when it didn't say.</param>
/// <param name="ClientProgress">Check 1 — how far the client had downloaded it, from 0 to 1; <c>null</c> when it didn't
/// say.</param>
/// <param name="SizeBytes">Check 2 — the size the disk reported; <c>null</c> before it was read.</param>
/// <param name="DurationMilliseconds">Check 2 — how long the media inspector says it plays.</param>
/// <param name="VideoCodec">Check 2 — its first video stream's codec, in the media inspector's names (<c>hevc</c>).</param>
/// <param name="Width">Check 2 — that stream's width in pixels.</param>
/// <param name="Height">Check 2 — that stream's height in pixels.</param>
/// <param name="CheckedUtc">Check 2 — when it was last read on the disk; <c>null</c> before.</param>
/// <param name="Kind">Check 4 — what the library made of it; <c>null</c> while it lists no item for it.</param>
/// <param name="SeasonNumber">Check 3 — an episode's season, as the library placed it.</param>
/// <param name="EpisodeNumber">Check 3 — an episode's number there.</param>
/// <param name="EpisodeNumberEnd">Check 3 — the last episode a file of several covers; <c>null</c> when the library
/// reports none.</param>
/// <param name="ReadUtc">When the library was last read for it; <c>null</c> before it was asked to look.</param>
/// <param name="WorkId">The work the library's ids identified it as, one of the replacement's works; <c>null</c> while
/// none is.</param>
/// <param name="Problem">Why it stopped the replacement, the app's own sentence; <c>null</c> when it didn't. While the check
/// still runs, what a pass that waits on another file found, which stops nothing yet (<paramref name="Progress"/> isn't
/// Stopped then).</param>
/// <param name="Progress">How far the check got with it.</param>
/// <param name="Identifiers">The registry ids the library gave its item when last read: its own, then its series'.</param>
public sealed record ReplacementJobNewFileDto(
    Guid Id,
    int Position,
    string RelativePath,
    string? AppPath,
    long? ClientSizeBytes,
    double? ClientProgress,
    long? SizeBytes,
    long? DurationMilliseconds,
    string? VideoCodec,
    int? Width,
    int? Height,
    DateTime? CheckedUtc,
    LibraryFileKind? Kind,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? EpisodeNumberEnd,
    DateTime? ReadUtc,
    Guid? WorkId,
    string? Problem,
    ReplacementNewFileProgress Progress,
    IReadOnlyList<ReplacementJobIdentifierDto> Identifiers);
