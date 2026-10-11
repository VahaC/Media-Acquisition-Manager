using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// MAM-27 — one new file of a replacement's download as a check of it left it (ADR 0035), for
/// <see cref="IReplacementJobDataFactory"/> and the planter: the client's word, the disk's reading, the library's item and
/// what it identified. What is left unset is evidence the check hadn't gathered — or, for a test of the job's page, a
/// stage it hadn't reached.
/// </summary>
/// <param name="RelativePath">Its name in the client's list of the download's files.</param>
/// <param name="ClientSizeBytes">Its size, as the client lists it.</param>
/// <param name="ClientProgress">How much of it the client has, from 0 to 1.</param>
/// <param name="AppPath">Where it is inside the app.</param>
/// <param name="Disk">Its identity as the disk gave it (device and inode, size, modification time).</param>
/// <param name="DurationMilliseconds">Its running time, as the media inspector read it.</param>
/// <param name="VideoCodec">Its video codec.</param>
/// <param name="Width">Its picture's width.</param>
/// <param name="Height">Its picture's height.</param>
/// <param name="CheckedUtc">When the disk and the media inspector read it.</param>
/// <param name="LibraryItemId">The library's item of it.</param>
/// <param name="Kind">What the library made of it.</param>
/// <param name="Refreshed">Whether the library finished reading it.</param>
/// <param name="SeasonNumber">An episode's season, as the library numbers it.</param>
/// <param name="EpisodeNumber">An episode's number there.</param>
/// <param name="EpisodeNumberEnd">The last episode the file covers.</param>
/// <param name="ReadUtc">When the check last read the library's item.</param>
/// <param name="MediaWorkId">The matched work it was identified as.</param>
/// <param name="Problem">Why it stopped the job.</param>
/// <param name="Identifiers">The registry ids the library gave it, an episode's series' ones marked so.</param>
public sealed record ReplacementNewFileFacts(
    string RelativePath,
    long? ClientSizeBytes = null,
    double? ClientProgress = null,
    string? AppPath = null,
    FileFacts? Disk = null,
    long? DurationMilliseconds = null,
    string? VideoCodec = null,
    int? Width = null,
    int? Height = null,
    DateTime? CheckedUtc = null,
    string? LibraryItemId = null,
    LibraryFileKind? Kind = null,
    bool Refreshed = false,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    int? EpisodeNumberEnd = null,
    DateTime? ReadUtc = null,
    Guid? MediaWorkId = null,
    string? Problem = null,
    IReadOnlyList<(MediaIdentifierScheme Scheme, string Value, bool OfSeries)>? Identifiers = null);
