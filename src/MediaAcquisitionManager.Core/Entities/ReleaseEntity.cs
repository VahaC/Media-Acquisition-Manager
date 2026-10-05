using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// A snapshot of one release as a search source listed it, stored when a search finds it (MAM-10), so the
/// release can be sent to a download client by its id (MAM-13) while its download link stays on the
/// server (ADR 0004), and a job's evidence of what was sent outlives the listing.
/// </summary>
/// <remarks>
/// <para>
/// A new row for every search that lists it, never an update of an earlier one: a download job points at
/// the snapshot it sent (Restrict), and that must not change when the listing is found again. One no job
/// points at is removed a day after it was stored, by the next search (<c>ReleaseStore</c>).
/// </para>
/// <para>
/// No path, no file list and no work: one release can hold several files and several works (the
/// three-movie pack in <c>VendorSamples/QBittorrent/torrent-files-multi.json</c>). Its files are known
/// only from the client once it has the release (MAM-14).
/// </para>
/// <para>
/// MAM-12 — a stored search's list filters, sorts and pages these rows rather than asking the sources
/// again (ADR 0011), so a row keeps what the list reads: the seeders and peers, its place in its source's
/// answer, and the search source that found it. It is still matched to no work in a column: which works a
/// release may be is read from its title and its listing's ids on every read, and listed, never stored
/// (ADR 0012). The ids are the listing's own claims; who claimed them is the row's own source columns.
/// </para>
/// </remarks>
public class ReleaseEntity : ProvenancedEntity
{
    /// <summary>The longest <see cref="CategoryIds"/> the column holds; the ids past it are left out,
    /// whole (MAM-32).</summary>
    public const int CategoryIdsMaximumLength = 512;

    /// <summary>The title as listed; every quality it names is a claim (specification §3.1).</summary>
    [Required, MaxLength(1000)]
    public string Title { get; set; } = default!;

    /// <summary>
    /// MAM-33 — the listing's own description when it says more than the title: Toloka's topic title,
    /// Ukrainian title first, where Prowlarr's title keeps the English part alone. Shown beside the title
    /// and read to find the work in a metadata catalog; <c>null</c> when the listing gives none, and on
    /// every row stored before MAM-33.
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>The size the listing claims; <c>null</c> when it gives none.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>When the source published it (a Torznab <c>pubDate</c>), in UTC.</summary>
    public DateTime? PublishedUtc { get; set; }

    /// <summary>The release's page at the source, with any session (<c>sid</c>) removed before it is
    /// stored (ADR 0004).</summary>
    [MaxLength(2048)]
    public string? SourcePageUrl { get; set; }

    /// <summary>
    /// What the release is downloaded from, which decides the compatible clients (§3.2, step 2);
    /// <c>null</c> when the listing doesn't say — a gateway link can turn out to be a magnet.
    /// </summary>
    public ReleasePayloadKind? PayloadKind { get; set; }

    /// <summary>
    /// The server-only download link or magnet, sealed like a credential with the purpose
    /// <c>release/{id:N}/payload-locator</c> (<c>ReleasePayloadLocatorPurpose</c>): a gateway's link
    /// carries a token of its own (Prowlarr's <c>link=</c>) and a magnet can carry tracker passkeys.
    /// Never returned and never logged. The gateway's API key is not in it: the adapter stores a
    /// placeholder in its place and puts the connection's current key back when it fetches (MAM-10). 8192
    /// because the links seen are 300 to 2000 characters long.
    /// </summary>
    [Required, MaxLength(8192)]
    public string PayloadLocatorEncrypted { get; set; } = default!;

    /// <summary>The info hash when the source states it (64 hex characters for v2), for correlating
    /// the client's job (§3.2, step 6).</summary>
    [MaxLength(64)]
    public string? InfoHash { get; set; }

    /// <summary>
    /// The stored search's source that found it (MAM-12); <c>null</c> for a release found by a plain
    /// release search (MAM-10), and once its search is removed while a download job keeps the release.
    /// </summary>
    public Guid? SearchSourceId { get; set; }

    /// <summary>Its 0-based place in its source's answer, in the source's own order (MAM-12): every order
    /// of a stored search's releases breaks a tie by its source's place in the search, then by this, and
    /// only then by the release's id, so the order is total.</summary>
    public int? Position { get; set; }

    /// <summary>The seeders the listing claims; <c>null</c> when it gives none.</summary>
    public int? Seeders { get; set; }

    /// <summary>The peers the listing claims: seeders and leechers together, as the source counts them
    /// (MAM-10).</summary>
    public int? Peers { get; set; }

    /// <summary>The IMDb id the listing gives (<c>tt0816692</c>), a claim like its title (MAM-12);
    /// <c>null</c> when it gives none or two.</summary>
    [MaxLength(64)]
    public string? ImdbId { get; set; }

    /// <summary>The TMDb id the listing gives (<c>157336</c>), a claim like its title (MAM-12);
    /// <c>null</c> when it gives none or two. A movie's on a movie release, a show's on a TV release
    /// (<see cref="Kind"/>, MAM-32): TMDb numbers the two separately.</summary>
    [MaxLength(64)]
    public string? TmdbId { get; set; }

    /// <summary>The TVDB id the listing gives (<c>341164</c>), the show's, a claim like its title
    /// (MAM-32); <c>null</c> when it gives none or two.</summary>
    [MaxLength(64)]
    public string? TvdbId { get; set; }

    /// <summary>Whether the source lists it as a movie or as TV (MAM-32), from its categories; a row
    /// stored before MAM-32 was a movie search's, so a movie.</summary>
    public ReleaseKind Kind { get; set; } = ReleaseKind.Movie;

    /// <summary>
    /// The source's categories it is listed in, comma-separated in the listing's order
    /// (<c>5040,100173</c>), so the Download dialog can pick the destination its tracker section goes to
    /// (MAM-32); <c>null</c> when it lists none, and for a row stored before MAM-32.
    /// </summary>
    [MaxLength(CategoryIdsMaximumLength)]
    public string? CategoryIds { get; set; }
}
