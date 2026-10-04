namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// Where one source places an episode work (MAM-32, ADR 0019): the series it belongs to and its season
/// and episode numbers, as the library item that carried them says.
/// </summary>
/// <remarks>
/// <para>
/// A fact with its source, like a title (ADR 0006), not columns on the work: two items of one episode
/// (the halves of a Mickey Mouse Clubhouse episode) each place it, and two sources that disagree about
/// its numbers both stay visible. One row per library item.
/// </para>
/// <para>
/// The series is the work its library series item landed on, never the library's series id itself: one
/// show can be several library series items (The Grand Tour is four), all on one series work (ADR 0017).
/// </para>
/// </remarks>
public class MediaEpisodePositionEntity : ProvenancedEntity
{
    /// <summary>The episode work; the position is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>
    /// The series work the episode belongs to; <c>null</c> when the library item names no series (the
    /// Tom &amp; Jerry Kids item, The Grand Tour's season-5 files) or its series was removed since.
    /// </summary>
    public Guid? SeriesWorkId { get; set; }

    /// <summary>The season number the source gives (0 for specials); <c>null</c> when it gives none.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>The episode number within the season; <c>null</c> when the source gives none.</summary>
    public int? EpisodeNumber { get; set; }
}
