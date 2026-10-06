using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — whether a link writes ids of its own into its library item, and why it stays in the app otherwise: the one
/// rule the dialog, a save, Retry and Write again, the background writer, the reconciler and the catch-up all ask, so
/// none of them can disagree about which links reach the library (Vasyl's decision of 2026-10-06).
/// </summary>
/// <remarks>
/// <para>
/// A link to the work itself is written when its library writes that kind of item — Jellyfin: a film or a series. An
/// episode's link to its show is written only on a film item (Jellyfin lists a documentary's episodes as movies), and only
/// with its season and episode: what it writes is the episode's own IMDb id, which TMDb gives for a numbered episode alone
/// — a TMDb id on a film item would name a film. An episode item no series holds keeps its link in the app, as the
/// library writes no episode.
/// </para>
/// <para>
/// Whether TMDb gives the episode an IMDb id is known only by asking it: a numbered episode's link counts as written here,
/// and the write asks TMDb unless the link holds its answer (<see cref="LibraryItemLinkEntity.EpisodeImdbId"/>, or
/// <see cref="NoEpisodeImdbReason"/> as its error).
/// </para>
/// </remarks>
public static class LibraryLinkWritability
{
    internal const string NoNumbersReason = "Without a season and an episode the app has no id of this episode to write.";

    /// <summary>
    /// Why a numbered episode's link stays in the app: TMDb gives that episode no IMDb id. Kept as the link's error, as
    /// only asking TMDb tells it — the catch-up leaves such a link alone, and the dialog says why.
    /// </summary>
    internal const string NoEpisodeImdbReason = "TMDb knows no IMDb id of this episode, so its link stays in the app.";

    /// <summary>A library type without the capability (ADR 0005) keeps its links in the app only.</summary>
    internal static string NoWriterReason(string name) => $"“{name}” can't set ids, so a link stays in the app.";

    /// <summary>
    /// Why the library <paramref name="name"/> doesn't take ids on an item of <paramref name="itemKind"/> — it can't set
    /// ids, or not on that kind (its own sentence) —; <c>null</c> when it does.
    /// </summary>
    public static string? ItemReason(IMediaLibraryIdentifierWriter? writer, string name, MediaWorkKind itemKind) =>
        writer is null ? NoWriterReason(name)
        : writer.WritableKinds.Contains(itemKind) ? null
        : writer.UnwritableKindReason(itemKind);

    /// <summary>
    /// Why a link of <paramref name="target"/> on an item its library writes stays in the app all the same: an episode's
    /// link without both its season and its episode; <c>null</c> when it writes ids of its own.
    /// </summary>
    public static string? LinkReason(LibraryLinkTarget target, int? seasonNumber, int? episodeNumber) =>
        target == LibraryLinkTarget.ShowEpisode && (seasonNumber is null || episodeNumber is null) ? NoNumbersReason : null;

    /// <summary>
    /// Why <paramref name="link"/> stays in the app, in the library <paramref name="name"/>; <c>null</c> when its write
    /// sends ids of its own. TMDb's "no IMDb id of this episode", once the link holds it, is one of the reasons.
    /// </summary>
    public static string? KeptReason(LibraryItemLinkEntity link, IMediaLibraryIdentifierWriter? writer, string name) =>
        ItemReason(writer, name, link.ItemKind)
        ?? LinkReason(link.Target, link.SeasonNumber, link.EpisodeNumber)
        ?? (KnowsNoEpisodeImdb(link) ? NoEpisodeImdbReason : null);

    /// <summary>Whether TMDb was asked for the episode's IMDb id of <paramref name="link"/> and gave none.</summary>
    public static bool KnowsNoEpisodeImdb(LibraryItemLinkEntity link) =>
        link is { Target: LibraryLinkTarget.ShowEpisode, EpisodeImdbId: null, WriteError: NoEpisodeImdbReason };
}
