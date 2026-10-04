using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One section of a release source — a category it lists releases in, such as Toloka's «Мультсеріали» —
/// whose releases the Download dialog sends to a destination unless the person picks another (MAM-32,
/// ADR 0020). The owner maps sections to destinations in Settings.
/// </summary>
/// <remarks>
/// <para>
/// By the source's own category id, never by a release's title: the tracker files a cartoon series
/// under «Мультсеріали» and a show under «Телесеріали», and that word is the one the owner's qBittorrent
/// categories follow (Cartoons, TV Shows). One section goes to one destination at most, so a release
/// never has two answers from one section.
/// </para>
/// <para>
/// A release lists both a standard category ("TV/HD") and the source's own one («Мультсеріали в HD»);
/// when both are mapped, the source's own wins (<see cref="IsSourceSpecific"/>): it says more.
/// </para>
/// </remarks>
public class DestinationSectionEntity : AuditableEntity
{
    /// <summary>The longest <see cref="CategoryId"/> the column holds.</summary>
    public const int CategoryIdMaximumLength = 32;

    /// <summary>The longest <see cref="Name"/> the column holds; a longer one is cut to it.</summary>
    public const int NameMaximumLength = 200;

    /// <summary>The destination its releases go to; the section is removed with it.</summary>
    public Guid DestinationMappingId { get; set; }

    /// <summary>The release search integration the section belongs to; removed with it.</summary>
    public Guid ReleaseSourceConnectionId { get; set; }

    /// <summary>The source's own id of the category (<c>100125</c>).</summary>
    [Required, MaxLength(CategoryIdMaximumLength)]
    public string CategoryId { get; set; } = default!;

    /// <summary>The category's name as the source gave it when the owner mapped it («Мультсеріали»), so
    /// the setting still reads after the source renames or drops it.</summary>
    [Required, MaxLength(NameMaximumLength)]
    public string Name { get; set; } = default!;

    /// <summary>Whether the category is the source's own section rather than a standard one shared by
    /// every source ("TV/HD"); the adapter knows its protocol's numbering.</summary>
    public bool IsSourceSpecific { get; set; }
}
