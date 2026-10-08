using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — one file name as <see cref="DuplicateGrouping"/> reads it: where it is, what the app's last look at it
/// read, and which works hold it. Two rows of one path (two libraries reporting it) are one name held by both.
/// </summary>
/// <param name="FileId">The name's row (the first, when two rows have its path).</param>
/// <param name="PathKey">What makes two names one: the app path in canonical form where it maps, else the path the
/// library gave; compared ordinally.</param>
/// <param name="DeviceId">The device its file is on, from the app's last look (ADR 0006); <c>null</c> when
/// unknown.</param>
/// <param name="Inode">Its file's inode, from the app's last look; <c>null</c> when unknown.</param>
/// <param name="LinkCount">How many names its file has, from the app's last look; <c>null</c> when unknown.</param>
/// <param name="SizeBytes">Its file's size as the app's last look read it — never the size a library reported;
/// <c>null</c> when unknown.</param>
/// <param name="Sha256">Its bytes' SHA-256, when a hash kept under the identity, size and time of the app's last look
/// exists (ADR 0022) — a hash of bytes since changed never counts; a name with a hash has its
/// <paramref name="SizeBytes"/>, the size the hash was kept under.</param>
/// <param name="Holders">The works holding it, each through a version; none for a name only the folder scan found
/// (MAM-18).</param>
/// <param name="DurationMilliseconds">The duration the media inspector measured; <c>null</c> when it didn't.</param>
/// <param name="DynamicRanges">The dynamic ranges of its video tracks the media inspector measured, each once; none
/// when unknown.</param>
/// <param name="Editions">The editions its file name and its versions' names say it is
/// (<see cref="EditionLabels"/>).</param>
/// <param name="NameClaims">What its path claims (<c>FileNameClaims</c>) — read for a name no library reported only,
/// the one way it can meet a work (a possible match); none otherwise.</param>
/// <param name="EpisodeSegment">MAM-37 — the segment letter its file name glues to its SxxEyy
/// (<see cref="EpisodeSegments"/>); read for every name, used by the same-work class only.</param>
/// <param name="ClaimedYear">MAM-37 — the year its file name claims in brackets (<see cref="FileNameYear"/>); read for
/// every name, used by the same-work class only.</param>
public sealed record DuplicateName(
    Guid FileId,
    string PathKey,
    long? DeviceId,
    long? Inode,
    int? LinkCount,
    long? SizeBytes,
    string? Sha256,
    IReadOnlyList<DuplicateHolder> Holders,
    long? DurationMilliseconds,
    IReadOnlyList<DynamicRange> DynamicRanges,
    IReadOnlyList<MediaEdition> Editions,
    IReadOnlyList<WorkClaim> NameClaims,
    string? EpisodeSegment,
    int? ClaimedYear)
{
    /// <summary>Whether no library reported the name: only the folder scan found it (MAM-18, ADR 0021).</summary>
    public bool IsUnreported => Holders.Count == 0;
}
