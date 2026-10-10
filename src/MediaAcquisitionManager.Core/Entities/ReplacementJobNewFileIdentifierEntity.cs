using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-25 — one registry id the library gave a <see cref="ReplacementJobNewFileEntity"/>'s item when it was last read
/// (ADR 0035): what check 3 compared with the job's <see cref="ReplacementJobWorkIdentifierEntity"/> rows. An episode
/// keeps its own ids and its series' apart (<see cref="OfSeries"/>): only the series' say which show it is (ADR 0017).
/// </summary>
public class ReplacementJobNewFileIdentifierEntity : BaseEntity
{
    /// <summary>The new file row; the id is removed with it.</summary>
    public Guid ReplacementJobNewFileId { get; set; }

    /// <summary>The registry.</summary>
    public MediaIdentifierScheme Scheme { get; set; }

    /// <summary>The id, as read.</summary>
    [Required, MaxLength(64)]
    public string Value { get; set; } = default!;

    /// <summary>Whether it is the id of the episode's series rather than of the file's own item.</summary>
    public bool OfSeries { get; set; }
}
