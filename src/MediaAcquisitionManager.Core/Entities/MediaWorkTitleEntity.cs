using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>A title one source gives a work, with the year it gives alongside.</summary>
public class MediaWorkTitleEntity : ProvenancedEntity
{
    /// <summary>The work; the title is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>The title exactly as the source wrote it ("Чужі").</summary>
    [Required, MaxLength(500)]
    public string Text { get; set; } = default!;

    /// <summary>Whether the source gave it as the displayed or the original title.</summary>
    public MediaTitleKind Kind { get; set; }

    /// <summary>
    /// The year this source gives with the title. On the title row rather than the work: title and
    /// year together are the second matching priority of specification §3.1, and the year needs a
    /// source as much as the title does.
    /// </summary>
    public int? Year { get; set; }
}
