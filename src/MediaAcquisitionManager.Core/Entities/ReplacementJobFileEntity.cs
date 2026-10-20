using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — one old copy the owner ticked for a <see cref="ReplacementJobEntity"/>, as it was when scheduled (ADR 0032):
/// which file of which version and work, where it is, and its identity read fresh from the disk at that moment.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, not keys: a sync replaces file rows, versions and works whenever the library reports an item anew, and a
/// key into them would rebuild those tables (ADR 0009) or lose the target — the
/// <see cref="LibraryItemLinkFileEntity"/> precedent. The ids say which rows they were, to be found again by path and
/// identity; nothing joins on them.
/// </para>
/// <para>
/// No <c>CreatedUtc</c>: a file row is part of its job, written with it and never alone.
/// </para>
/// <para>
/// MAM-25 — the last four columns are the verifier's (ADR 0035), written on each of its passes: what the library and
/// the app's own readings said of the copy, the new file it was paired with, and why it stopped the job. Nullable
/// columns added in place, with no key, so the upgrade rebuilds no table.
/// </para>
/// <para>
/// MAM-27 — the last two are the owner's drop and the verifier's mark of a problem only a drop settles, added in place the
/// same way (the flag with a default), so every copy an upgrade finds reads as not dropped and as one a Retry may settle.
/// </para>
/// </remarks>
public class ReplacementJobFileEntity : BaseEntity
{
    /// <summary>The longest <see cref="Problem"/> kept, as the job's own reason; a longer message is fitted to it (MAM-47:
    /// its longest value cut).</summary>
    public const int ProblemMaximumLength = ReplacementJobEntity.ReasonMaximumLength;

    /// <summary>The job; the row is removed with it.</summary>
    public Guid ReplacementJobId { get; set; }

    /// <summary>The 0-based position among the job's files, in the dialog's order.</summary>
    public int Position { get; set; }

    /// <summary>The file row it was picked as.</summary>
    public Guid PhysicalFileId { get; set; }

    /// <summary>The version holding it.</summary>
    public Guid MediaVersionId { get; set; }

    /// <summary>The work it is a copy of: the film, or the episode.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>The work the release matched: the film, or the episode's series.</summary>
    public Guid MatchedWorkId { get; set; }

    /// <summary>An episode's season, as its own library item places it inside the release's claim; <c>null</c> for a
    /// film.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>An episode's number there; <c>null</c> for a film, and for an episode the library numbers none.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>The path as its library reported it.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string ServicePath { get; set; } = default!;

    /// <summary>The path inside the app, canonical, through the library's mappings when it was scheduled.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string AppPath { get; set; } = default!;

    /// <summary><c>st_dev</c>, read from the disk when it was scheduled, stored bit for bit like
    /// <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long DeviceId { get; set; }

    /// <summary><c>st_ino</c>, read with <see cref="DeviceId"/>.</summary>
    public long Inode { get; set; }

    /// <summary>The size the disk reported then.</summary>
    public long SizeBytes { get; set; }

    /// <summary>The modification time the disk reported then, to the tick.</summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>How many names the file had then; <c>null</c> when the platform doesn't say.</summary>
    public int? LinkCount { get; set; }

    /// <summary>The version's own edition label then; <c>null</c> when the library gave none.</summary>
    [MaxLength(MediaVersionEntity.EditionLabelMaximumLength)]
    public string? EditionLabel { get; set; }

    /// <summary>
    /// MAM-25 — the <see cref="ReplacementJobNewFileEntity"/> the copy is paired with: set once the job is verified, on
    /// the copies that paired before the job stopped for another, and on a copy whose pair itself stopped it (another
    /// edition, too short), beside its <see cref="Problem"/>; <c>null</c> otherwise. No key: a key added to this table
    /// would rebuild it, and the new file rows are replaced whole on every check of the files.
    /// </summary>
    public Guid? PairedNewFileId { get; set; }

    /// <summary>
    /// MAM-25 — the last episode the copy's own library item covers, as the library last reported it (a file of two
    /// episodes ends one after <see cref="EpisodeNumber"/>); <c>null</c> for a film, before the library is read, and when
    /// the library gives none. ADR 0032: a copy of several episodes is never replaced by one new file.
    /// </summary>
    public int? EpisodeNumberEnd { get; set; }

    /// <summary>
    /// MAM-25 — the copy's measured running time in milliseconds, from the media inspector's cached reading of its
    /// identity when it was checked; <c>null</c> when it was never read, which leaves the new file's running time
    /// uncompared (Vasyl, 2026-10-10).
    /// </summary>
    public long? DurationMilliseconds { get; set; }

    /// <summary>MAM-25 — why this copy stopped the job, as a message the Api renders (MAM-47, ADR 0042); <c>null</c> when
    /// it didn't.</summary>
    [MaxLength(ProblemMaximumLength)]
    public Message? Problem { get; set; }

    /// <summary>
    /// MAM-27 — when the owner dropped the copy from the job; <c>null</c> while it is in it. A dropped copy stays where it
    /// is: the check no longer pairs it or waits on it, the mover never moves it, and another download may tick it again.
    /// Its own column, never <see cref="Problem"/> or <see cref="PairedNewFileId"/>, which every check of the files wipes;
    /// the row itself stays, as the drop's history and as the ticks the request was sent with. Written by the owner's Drop
    /// alone, in one conditional <c>UPDATE</c>, and never cleared.
    /// </summary>
    public DateTime? DroppedUtc { get; set; }

    /// <summary>
    /// MAM-27 — whether the copy's <see cref="Problem"/> is one no fix in the library or the download client can change —
    /// an episode the library numbers apart from the one ticked, a new file under ¾ of its running time, a cut the release
    /// didn't claim, a half or several episodes in one file —, so only dropping it, or keeping both copies, settles it,
    /// and Retry is withheld while an undropped copy has it. Written by the check beside the problem and cleared with it;
    /// <c>false</c> when the copy has no problem.
    /// </summary>
    public bool OnlyDropSettles { get; set; }
}
