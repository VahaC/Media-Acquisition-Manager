using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One version of a work as a source reports it: a cut, an edition, a quality. A work has any number
/// of versions, and a version any number of files.
/// </summary>
/// <remarks>
/// The inherited <see cref="ProvenancedEntity.SourceItemId"/> is the library item. A library that
/// keeps two versions as two items gives two versions with different item ids; one that groups them
/// under one item gives two versions sharing the item id with different
/// <see cref="SourceVersionId"/>s.
/// </remarks>
public class MediaVersionEntity : ProvenancedEntity
{
    /// <summary>The longest <see cref="SourceVersionId"/> the column holds; an adapter never cuts an id,
    /// it takes a longer one for none (MAM-8).</summary>
    public const int SourceVersionIdMaximumLength = 256;

    /// <summary>The longest <see cref="EditionLabel"/> the column holds; an adapter cuts a longer one
    /// to it (MAM-8).</summary>
    public const int EditionLabelMaximumLength = 200;

    /// <summary>The work; the version is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>
    /// The source's id of this version (a library's media-source id), unique across the source, not
    /// only inside its item: a library sync keys versions by it whatever item lists them (MAM-8,
    /// ADR 0008). <c>null</c> when the source has no such notion.
    /// </summary>
    [MaxLength(SourceVersionIdMaximumLength)]
    public string? SourceVersionId { get; set; }

    /// <summary>
    /// The edition as the source names it ("Special Edition"): a possible distinction between
    /// versions (specification §3.3), never parsed into a verdict.
    /// </summary>
    [MaxLength(EditionLabelMaximumLength)]
    public string? EditionLabel { get; set; }
}
