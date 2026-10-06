using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — the owner's TMDb link of one library item: "this item is that film, that show, or an episode of that
/// show", set by hand, kept per item (an integration and its item id) and counted like the library's own ids.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own, not a <see cref="ProvenancedEntity"/> row among the work's identifiers: its source is a person,
/// and the integration and the item are its <em>target</em>. A sync never offers a work holding another source's fact
/// to its items (ADR 0008), so a link stored as an identifier would split the item off its own work at the next sync,
/// and the purge and the empty-work delete would keep or drop it by rules written for library facts. Here nothing of
/// the sync's provenance machinery sees it; the sync re-applies it to whatever work the item lands on
/// (<see cref="MediaWorkId"/>).
/// </para>
/// <para>
/// One row per item, whichever work the item sits on: a work holding two items (the two "Чужі") gets a link per item,
/// so each item's library is written on its own and a later sync can split them apart again.
/// </para>
/// </remarks>
public class LibraryItemLinkEntity : AuditableEntity
{
    /// <summary>The longest item id kept, as <see cref="ProvenancedEntity.SourceItemId"/>.</summary>
    public const int SourceItemIdMaximumLength = 512;

    /// <summary>The longest id value kept, as <see cref="MediaWorkIdentifierEntity.Value"/>.</summary>
    public const int ValueMaximumLength = 64;

    /// <summary>The longest <see cref="CatalogTitle"/> and <see cref="ItemTitle"/> kept, as a library title's.</summary>
    public const int TitleMaximumLength = MediaWorkTitleEntity.TextMaximumLength;

    /// <summary>The longest <see cref="WriteError"/> the column holds; a longer sentence is cut to it.</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>
    /// The library integration the item is in. The link is removed with it (decision 12): a re-added integration gets a
    /// new id, so its old links could never find their items again.
    /// </summary>
    public Guid IntegrationConnectionId { get; set; }

    /// <summary>The library's own id of the item, as its facts' <see cref="ProvenancedEntity.SourceItemId"/>.</summary>
    [Required, MaxLength(SourceItemIdMaximumLength)]
    public string SourceItemId { get; set; } = default!;

    /// <summary>What the item was when it was linked: a film, a show or an episode.</summary>
    public MediaWorkKind ItemKind { get; set; }

    /// <summary>
    /// The work the item sat on after the last sync or re-attach; <c>null</c> while the library reports no such item
    /// ("unattached"). Set null, never deleted, when a sync deletes the work.
    /// </summary>
    public Guid? MediaWorkId { get; set; }

    /// <summary>Whether the item is the catalog work or an episode of the catalog show.</summary>
    public LibraryLinkTarget Target { get; set; }

    /// <summary>The number space of <see cref="Value"/>: a TMDb film (<see cref="MediaIdentifierScheme.Tmdb"/>) or show
    /// (<see cref="MediaIdentifierScheme.TmdbTv"/>).</summary>
    public MediaIdentifierScheme Scheme { get; set; }

    /// <summary>TMDb's id of the film or show ("223021").</summary>
    [Required, MaxLength(ValueMaximumLength)]
    public string Value { get; set; } = default!;

    /// <summary>The season of the show an episode-to-show link names; <c>null</c> when it names none, and for a link to
    /// the work itself.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>The episode of <see cref="SeasonNumber"/>; never without a season.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>TMDb's title of the film or show (in Ukrainian) when it was linked, copied for display.</summary>
    [MaxLength(TitleMaximumLength)]
    public string? CatalogTitle { get; set; }

    /// <summary>
    /// The item's display title when it was linked (≤ 500), copied so a link whose item the library no longer reports
    /// still says what it was.
    /// </summary>
    [MaxLength(TitleMaximumLength)]
    public string? ItemTitle { get; set; }

    /// <summary>The IMDb id TMDb gave for the film or show; <c>null</c> when it gave none.</summary>
    [MaxLength(ValueMaximumLength)]
    public string? ImdbId { get; set; }

    /// <summary>
    /// MAM-34 — for an episode-to-show link with its season and episode, the episode's own IMDb id as TMDb gave it: the
    /// one id of the episode a film item can be given — its <c>Tmdb</c> key names a film —, so what the app writes into
    /// the item. <c>null</c> while TMDb wasn't asked, when it gives none, and for any other link.
    /// </summary>
    [MaxLength(ValueMaximumLength)]
    public string? EpisodeImdbId { get; set; }

    /// <summary>The catalog integration that gave <see cref="ImdbId"/>; <c>null</c> once it is removed (its name stays).</summary>
    public Guid? ImdbSourceConnectionId { get; set; }

    /// <summary>That integration's name at the time (the audit-row pattern).</summary>
    [MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string? ImdbSourceName { get; set; }

    /// <summary>When the catalog gave <see cref="ImdbId"/>.</summary>
    public DateTime? ImdbObservedUtc { get; set; }

    /// <summary>
    /// The library's own id of <see cref="Scheme"/> this link replaces ("a link replaces a wrong id"): what the item
    /// held when the link was set and that differs from <see cref="Value"/>. Readers and the grouping drop that value of
    /// this item only while the link isn't confirmed in the library (R6); <c>null</c> when there was none.
    /// </summary>
    [MaxLength(ValueMaximumLength)]
    public string? SupersededValue { get; set; }

    /// <summary>The user who set it; <c>null</c> once that user is deleted.</summary>
    public Guid? SetByUserId { get; set; }

    /// <summary>The setter's user name at the time, copied (the audit-row pattern).</summary>
    [Required, MaxLength(UserNameRules.MaximumLength)]
    public string SetByUserName { get; set; } = default!;

    /// <summary>When it was set, or last replaced.</summary>
    public DateTime SetUtc { get; set; }

    /// <summary>
    /// The "Accept all confident" click that set it, shared by every link of that click so the page can poll their
    /// results; <c>null</c> for a link set from the dialog.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>Where it stands against the library it would be written into.</summary>
    public LibraryLinkWriteState WriteState { get; set; }

    /// <summary>
    /// A new id per claim of the link for a write (R9): the claimer's final update matches it, so a write that ran
    /// late can't overwrite the state a later write already set.
    /// </summary>
    public Guid WriteAttemptId { get; set; }

    /// <summary>A readable sentence saying why the library doesn't hold it, the library's secrets masked; shown to the
    /// owner only. <c>null</c> unless <see cref="WriteState"/> says something went wrong — or, for an episode's link kept
    /// in the app because TMDb gives its episode no IMDb id, that sentence, as only asking TMDb tells it.</summary>
    [MaxLength(ErrorMaximumLength)]
    public string? WriteError { get; set; }

    /// <summary>When <see cref="WriteState"/> last changed; <c>null</c> before it ever did.</summary>
    public DateTime? WriteStateUtc { get; set; }

    /// <summary>
    /// MAM-34 — when the app last asked the library to look up the item's missing metadata, right after a write that
    /// left the link's ids in it; <c>null</c> while it never did — a link written before the app asked
    /// (<c>GET api/library/tmdb/catch-up</c> counts those), one not written, or one kept in the app.
    /// </summary>
    public DateTime? LookupRequestedUtc { get; set; }

    /// <summary>
    /// MAM-34 — why the library didn't take that request, as a readable sentence with the library's secrets masked;
    /// shown to the owner only, who asks again with Retry. <c>null</c> when it took it. A failed lookup never changes
    /// <see cref="WriteState"/>: the ids are in. Also why it couldn't be asked, when a write whose ids the last sync read
    /// in the item reached nothing (the library unreachable or refusing): the link stays in the library, its lookup
    /// missed.
    /// </summary>
    [MaxLength(ErrorMaximumLength)]
    public string? LookupError { get; set; }
}
