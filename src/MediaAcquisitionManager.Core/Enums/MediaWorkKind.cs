namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What a media work is. Stored by name, so a member added later needs no migration.
/// </summary>
/// <remarks>
/// Movies only in Phase A. MAM-32 adds the episode, a work of its own with versions and files, and the
/// series, the work its episodes belong to, which holds the show's titles and ids and no version
/// (ADR 0017, ADR 0019). Whether an item is one or the other is the library's word alone.
/// </remarks>
public enum MediaWorkKind
{
    /// <summary>A film.</summary>
    Movie = 1,

    /// <summary>One episode of a series, as the library lists it.</summary>
    Episode = 2,

    /// <summary>A show: its episodes point at it (<see cref="Entities.MediaEpisodePositionEntity"/>), and it
    /// holds no version of its own.</summary>
    Series = 3,
}
