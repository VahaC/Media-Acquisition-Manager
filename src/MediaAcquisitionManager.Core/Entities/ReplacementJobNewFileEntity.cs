using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-25 — one new video file a <see cref="ReplacementJobEntity"/>'s download brought, and what each check found of it
/// (ADR 0035): the client's word (check 1), the file on the disk and the media inspector's reading (check 2), the item
/// the library made of it and its ids (checks 4 and 3), and the work it was identified as or why it stopped the job. The
/// evidence a stopped job shows, and the pairs a verified one moves by in MAM-26.
/// </summary>
/// <remarks>
/// <para>
/// One row per file the client downloads with a video file's extension, at its place in the client's list. The rows are
/// replaced whole each time the files' checks run, and updated by each read of the library: what was read last, never
/// a history. Every value not read yet, or not given, is <c>null</c>.
/// </para>
/// <para>
/// Not <c>MediaInspections</c>: an inspection run forgets readings no file name points at, and this evidence must stay
/// with its job. No key into the library's integration or works: a snapshot, as the job's other rows are. No
/// <c>CreatedUtc</c>: part of its job, written with it and never alone.
/// </para>
/// </remarks>
public class ReplacementJobNewFileEntity : BaseEntity
{
    /// <summary>The longest <see cref="Problem"/> kept, as the job's own reason; a longer message is fitted to it (MAM-47:
    /// its longest value cut).</summary>
    public const int ProblemMaximumLength = ReplacementJobEntity.ReasonMaximumLength;

    /// <summary>The longest library item id kept: a library's ids are short (Jellyfin's are 32 hexadecimal digits),
    /// and 256 leaves room for another library's.</summary>
    public const int ItemIdMaximumLength = 256;

    /// <summary>The job; the row is removed with it.</summary>
    public Guid ReplacementJobId { get; set; }

    /// <summary>The 0-based position of the file among the download's files, in the client's own order.</summary>
    public int Position { get; set; }

    /// <summary>
    /// The file's path relative to the download's folder in the client, <c>/</c>-separated and exactly as the client
    /// names it (<see cref="DownloadJobFileEntity.RelativePath"/>).
    /// </summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string RelativePath { get; set; } = default!;

    /// <summary>Check 1 — the file's size as the client reported it; <c>null</c> when it didn't say.</summary>
    public long? ClientSizeBytes { get; set; }

    /// <summary>Check 1 — how far the client had downloaded it, from 0 to 1; <c>null</c> when it didn't say.</summary>
    public double? ClientProgress { get; set; }

    /// <summary>
    /// The file's path inside the app, canonical: the client's save folder joined with <see cref="RelativePath"/>,
    /// through the client's path mappings; <c>null</c> when they give none.
    /// </summary>
    [MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string? AppPath { get; set; }

    /// <summary>The library integration whose folder holds the file; <c>null</c> when none does. No key: a
    /// snapshot.</summary>
    public Guid? LibraryIntegrationId { get; set; }

    /// <summary>The file's path in that library's own terms, what it is asked to look at and read by; <c>null</c> when
    /// no library folder holds it.</summary>
    [MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string? LibraryPath { get; set; }

    /// <summary>Check 2 — <c>st_dev</c> of the file, stored bit for bit like <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long? DeviceId { get; set; }

    /// <summary>Check 2 — <c>st_ino</c> of the file, read with <see cref="DeviceId"/>.</summary>
    public long? Inode { get; set; }

    /// <summary>Check 2 — the size the disk reported.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Check 2 — the modification time the disk reported, to the tick.</summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>Check 2 — how long the media inspector says it plays, in milliseconds.</summary>
    public long? DurationMilliseconds { get; set; }

    /// <summary>Check 2 — the first video stream's codec, in the media inspector's names (<c>hevc</c>).</summary>
    [MaxLength(MediaInspectionTrackEntity.NameMaximumLength)]
    public string? VideoCodec { get; set; }

    /// <summary>Check 2 — the first video stream's width in pixels.</summary>
    public int? Width { get; set; }

    /// <summary>Check 2 — the first video stream's height in pixels.</summary>
    public int? Height { get; set; }

    /// <summary>Check 2 — when the file was last read from the disk.</summary>
    public DateTime? CheckedUtc { get; set; }

    /// <summary>Check 4 — the id of the file's own item in the library, a hidden version of another item included;
    /// <c>null</c> while the library lists none.</summary>
    [MaxLength(ItemIdMaximumLength)]
    public string? LibraryItemId { get; set; }

    /// <summary>Check 4 — what the library made of the file; <c>null</c> while it lists no item for it.</summary>
    public LibraryFileKind? Kind { get; set; }

    /// <summary>Check 4 — whether the library had finished reading the file into its item: it gave a running time, and
    /// an episode its number.</summary>
    public bool Refreshed { get; set; }

    /// <summary>Check 3 — an episode's series item in the library; <c>null</c> for anything else, or when the library
    /// gives none.</summary>
    [MaxLength(ItemIdMaximumLength)]
    public string? SeriesItemId { get; set; }

    /// <summary>Check 3 — an episode's season number, as the library places it.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Check 3 — an episode's number in its season, as the library places it.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Check 3 — the last episode a file of several covers, as the library reports it; <c>null</c> when it
    /// reports none.</summary>
    public int? EpisodeNumberEnd { get; set; }

    /// <summary>When the library was last read for the file; <c>null</c> before it was asked to look.</summary>
    public DateTime? ReadUtc { get; set; }

    /// <summary>The matched work the library's ids identified the file as (the job's
    /// <see cref="ReplacementJobWorkEntity.MediaWorkId"/>); <c>null</c> while none is. No key: a snapshot.</summary>
    public Guid? MediaWorkId { get; set; }

    /// <summary>Why this file stopped the job, as a message the Api renders (MAM-47, ADR 0042); <c>null</c> when it
    /// didn't.</summary>
    [MaxLength(ProblemMaximumLength)]
    public Message? Problem { get; set; }
}
