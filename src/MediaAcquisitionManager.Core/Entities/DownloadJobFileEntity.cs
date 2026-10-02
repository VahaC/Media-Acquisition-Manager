using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One file of a download job, as its client last listed it (MAM-14): where it goes under the job's folder,
/// how big it is, how far it has come, and whether the client downloads it at all. A value the client didn't
/// give is <c>null</c>.
/// </summary>
/// <remarks>
/// No <c>CreatedUtc</c>: a file row is part of its job, written and removed with it, and replaced as a whole
/// list when the client's differs. What the client says, not what is on disk: the library's files are
/// <see cref="PhysicalFileEntity"/>'s.
/// </remarks>
public class DownloadJobFileEntity : BaseEntity
{
    /// <summary>The job; the file row is removed with it.</summary>
    public Guid DownloadJobId { get; set; }

    /// <summary>The 0-based position among the job's files, in the client's own order.</summary>
    public int Position { get; set; }

    /// <summary>
    /// The file's path relative to the job's folder in the client, <c>/</c>-separated and exactly as the
    /// client names it — the root folder included under qBittorrent's "Original" layout. As long as a path
    /// column holds (<see cref="PhysicalFileEntity.ServicePathMaximumLength"/>): the adapter takes a longer one
    /// for no answer at all rather than cut it.
    /// </summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string RelativePath { get; set; } = default!;

    /// <summary>Its size in bytes.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>How far the client has downloaded it, from 0 to 1.</summary>
    public double? Progress { get; set; }

    /// <summary>Whether the client downloads it; <c>false</c> for a file someone skipped in the client.</summary>
    public bool? IsWanted { get; set; }
}
