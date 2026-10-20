using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — whether a link is identified in its library item, and why it stays in the app otherwise: the one rule the
/// dialog, a save, Retry and Write again, the background writer, the reconciler and the repair all ask, so none of them
/// can disagree about which links reach the library (Vasyl's decisions of 2026-10-06; ADR 0026).
/// </summary>
/// <remarks>
/// <para>
/// A link to the work itself is identified when its library identifies that kind of item — Jellyfin: a film or a series.
/// An episode's link to its show is identified only on a film item (Jellyfin lists a documentary's episodes as movies),
/// and only with its season and episode: what the item is identified by is the episode's own IMDb id, which TMDb gives
/// for a numbered episode alone — a TMDb id on a film item would name a film. An episode item no series holds keeps its
/// link in the app, as the library identifies no episode.
/// </para>
/// <para>
/// Whether TMDb gives the episode an IMDb id is known only by asking it: a numbered episode's link counts as identified
/// here, and the identification asks TMDb unless the link holds its answer (<see cref="LibraryItemLinkEntity.EpisodeImdbId"/>,
/// or <see cref="NoEpisodeImdbReason"/> as its error, known by its key).
/// </para>
/// </remarks>
public static class LibraryLinkWritability
{
    internal static readonly Message NoNumbersReason = Message.Of("library-links.kept.no-numbers");

    /// <summary>
    /// MAM-47 (ADR 0042) — the key of <see cref="NoEpisodeImdbReason"/>: the link's error is a flag as well as a reason, and
    /// the flag is its key, the same in every language and whatever the values (it has none).
    /// </summary>
    internal const string NoEpisodeImdbKey = "library-links.kept.no-episode-imdb";

    /// <summary>
    /// Why a numbered episode's link stays in the app: TMDb gives that episode no IMDb id. Kept as the link's error, as
    /// only asking TMDb tells it — the repair leaves such a link alone, and the dialog says why.
    /// </summary>
    internal static readonly Message NoEpisodeImdbReason = Message.Of(NoEpisodeImdbKey);

    /// <summary>A library type without the capability (ADR 0005) keeps its links in the app only.</summary>
    internal static Message NoIdentifierReason(string name) => Message.Of("library-links.kept.no-identifier", ("integration", name));

    /// <summary>
    /// Why the library <paramref name="name"/> doesn't identify an item of <paramref name="itemKind"/> — it can't identify
    /// items, or not that kind (its own message) —; <c>null</c> when it does.
    /// </summary>
    public static Message? ItemReason(IMediaLibraryItemIdentifier? identifier, string name, MediaWorkKind itemKind) =>
        identifier is null ? NoIdentifierReason(name)
        : identifier.IdentifiableKinds.Contains(itemKind) ? null
        : identifier.UnidentifiableKindReason(itemKind);

    /// <summary>
    /// Why a link of <paramref name="target"/> on an item its library identifies stays in the app all the same: an
    /// episode's link without both its season and its episode; <c>null</c> when it identifies the item.
    /// </summary>
    public static Message? LinkReason(LibraryLinkTarget target, int? seasonNumber, int? episodeNumber) =>
        target == LibraryLinkTarget.ShowEpisode && (seasonNumber is null || episodeNumber is null) ? NoNumbersReason : null;

    /// <summary>
    /// Why <paramref name="link"/> stays in the app, in the library <paramref name="name"/>; <c>null</c> when it identifies
    /// its item. TMDb's "no IMDb id of this episode", once the link holds it, is one of the reasons.
    /// </summary>
    public static Message? KeptReason(LibraryItemLinkEntity link, IMediaLibraryItemIdentifier? identifier, string name) =>
        ItemReason(identifier, name, link.ItemKind)
        ?? LinkReason(link.Target, link.SeasonNumber, link.EpisodeNumber)
        ?? (KnowsNoEpisodeImdb(link) ? NoEpisodeImdbReason : null);

    /// <summary>Whether TMDb was asked for the episode's IMDb id of <paramref name="link"/> and gave none.</summary>
    /// <remarks>MAM-47 (ADR 0042) — by the error's key: the column holds key and values, and the startup converter turns
    /// the stored English of an older build into this key before any request.</remarks>
    public static bool KnowsNoEpisodeImdb(LibraryItemLinkEntity link) =>
        link is { Target: LibraryLinkTarget.ShowEpisode, EpisodeImdbId: null } && link.WriteError?.Key == NoEpisodeImdbKey;
}
