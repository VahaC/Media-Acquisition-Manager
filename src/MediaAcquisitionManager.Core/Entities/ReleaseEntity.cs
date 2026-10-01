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
/// points at is removed a day after it was stored, by the next search (<c>ReleaseSearchService</c>).
/// </para>
/// <para>
/// No path, no file list and no work: one release can hold several files and several works (the
/// three-movie pack in <c>VendorSamples/QBittorrent/torrent-files-multi.json</c>). Its files are known
/// only from the client once it has the release (MAM-14).
/// </para>
/// </remarks>
public class ReleaseEntity : ProvenancedEntity
{
    /// <summary>The title as listed; every quality it names is a claim (specification §3.1).</summary>
    [Required, MaxLength(1000)]
    public string Title { get; set; } = default!;

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
}
