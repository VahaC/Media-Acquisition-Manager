using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>A title one source gives a work, with the year it gives alongside.</summary>
public class MediaWorkTitleEntity : ProvenancedEntity
{
    /// <summary>The longest title <see cref="Text"/> and <see cref="NormalizedText"/> hold; an adapter
    /// cuts a longer one to it (MAM-8).</summary>
    public const int TextMaximumLength = 500;

    /// <summary>The work; the title is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>The title exactly as the source wrote it ("Чужі").</summary>
    [Required, MaxLength(TextMaximumLength)]
    public string Text { get; set; } = default!;

    /// <summary>
    /// <see cref="LibrarySearchText.Normalize"/> of <see cref="Text"/>, which the library search matches
    /// (MAM-8): SQLite folds only ASCII letters, so "чужі" would never find "Чужі" by
    /// <see cref="Text"/>.
    /// </summary>
    [Required, MaxLength(TextMaximumLength)]
    public string NormalizedText { get; set; } = default!;

    /// <summary>Whether the source gave it as the displayed or the original title.</summary>
    public MediaTitleKind Kind { get; set; }

    /// <summary>
    /// The year this source gives with the title. On the title row rather than the work: title and
    /// year together are the second matching priority of specification §3.1, and the year needs a
    /// source as much as the title does.
    /// </summary>
    public int? Year { get; set; }
}
