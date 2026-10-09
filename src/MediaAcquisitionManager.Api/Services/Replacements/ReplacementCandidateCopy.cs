using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — one old copy a matched release may replace, as one matched work lists it (ADR 0032): a file of one of the
/// work's versions — a film's, or a claimed episode's —, where the app finds it, what it holds, and why it can't be
/// ticked.
/// </summary>
/// <param name="Id">
/// The id the owner ticks it by: the first file row listed at its path. One file listed under several works — two
/// libraries over one folder — is one id everywhere, so ticking it once ticks it under each, and every work's
/// confirmation counts for it (R3).
/// </param>
/// <param name="File">The file as the Library page shows it, the owner's sentences included, under <paramref name="Id"/>.</param>
/// <param name="Row">This listing's own file row.</param>
/// <param name="Version">The version holding it.</param>
/// <param name="WorkId">The work it is a copy of: the film, or the episode.</param>
/// <param name="MatchedWorkId">The work the release matched: the film, or the episode's series.</param>
/// <param name="SeasonNumber">An episode's season as its version's own library item places it; <c>null</c> for a film.</param>
/// <param name="EpisodeNumber">An episode's number there; <c>null</c> for a film.</param>
/// <param name="AppPath">Where the app finds it, through its library's mappings now; <c>null</c> when they give it no
/// path.</param>
/// <param name="Editions">MAM-37 — the edition words its name and its version's label hold.</param>
/// <param name="DynamicRanges">The dynamic ranges ffprobe measured in its video, each once; none when unknown.</param>
/// <param name="Fresh">Its identity as the disk gave it during this read; <c>null</c> when it wasn't read — a copy that
/// failed a stored check first — or wasn't there.</param>
/// <param name="Unavailable">Why it can't be ticked, the strictest of every listing at its path; <c>null</c> when it
/// can.</param>
public sealed record ReplacementCandidateCopy(
    Guid Id,
    LibraryFileDto File,
    PhysicalFileEntity Row,
    MediaVersionEntity Version,
    Guid WorkId,
    Guid MatchedWorkId,
    int? SeasonNumber,
    int? EpisodeNumber,
    NormalizedPath? AppPath,
    IReadOnlyList<MediaEdition> Editions,
    IReadOnlyList<DynamicRange> DynamicRanges,
    FileFacts? Fresh,
    ReplacementCopyUnavailableReason? Unavailable)
{
    /// <summary>
    /// R2 — the file has more names than this one (a hardlink), so moving it frees no space: said of every copy that can
    /// be ticked, whoever holds the other name — a torrent's own name usually lies outside the Media folders, where the
    /// app can't look.
    /// </summary>
    public bool OtherNames => Unavailable is null && Fresh is { LinkCount: > 1 };
}
