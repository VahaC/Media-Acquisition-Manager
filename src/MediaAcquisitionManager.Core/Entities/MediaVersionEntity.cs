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
    /// <summary>The work; the version is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>
    /// The source's id of this version inside its item, when one item groups several versions (a
    /// library's media-source id); <c>null</c> when the source has no such notion.
    /// </summary>
    [MaxLength(256)]
    public string? SourceVersionId { get; set; }

    /// <summary>
    /// The edition as the source names it ("Special Edition"): a possible distinction between
    /// versions (specification §3.3), never parsed into a verdict.
    /// </summary>
    [MaxLength(200)]
    public string? EditionLabel { get; set; }
}
