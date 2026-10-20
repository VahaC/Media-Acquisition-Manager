using System.Globalization;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — setting or replacing a work's link (the dialog's Save) and "Accept all confident"'s links: one way of saving
// them, so a link set from the dialog and one set by the click are the same rows with the same audit rows. Saving never
// calls the library: each link that is identified by rule is saved Queued and the background writer identifies it
// (ADR 0026) — an Apply takes seconds for a film and minutes for a series, which no request waits for.
public sealed partial class LibraryLinkService
{
    // MAM-47 (ADR 0042) — the refusals and skip reasons LibraryLinkSuggestionService says too (its free search, its pasted
    // id, "Accept all confident") are internal and used from there: one key per wording, never a copy of the words.
    internal static readonly Message KindMissingError = Message.Of("library-links.set.kind-missing");
    internal static readonly Message IdMissingError = Message.Of("library-links.set.id-missing");
    private static readonly Message EpisodeWithoutSeasonError = Message.Of("library-links.set.episode-without-season");
    private static readonly Message NumbersNotAllowedError = Message.Of("library-links.set.numbers-not-allowed");
    private static readonly Message FilmForShowError = Message.Of("library-links.set.film-for-show");
    private static readonly Message SeriesEpisodeError = Message.Of("library-links.set.series-episode");
    internal static readonly Message WorkGoneError = Message.Of("library-links.batch.work-gone");
    internal static readonly Message LinkedMeanwhileError = Message.Of("library-links.batch.linked-meanwhile");

    /// <summary>The highest season number taken: TMDb numbers seasons from 0 (the specials) up.</summary>
    private const int MaximumSeasonNumber = 999;

    /// <summary>The highest episode number taken: the longest-running shows count episodes in the thousands.</summary>
    private const int MaximumEpisodeNumber = 9999;

    internal static Message KindInvalidError(string kind) => Message.Of("library-links.set.kind-invalid", ("value", Echo(kind)));

    internal static Message IdInvalidError(string id) => Message.Of("library-links.set.id-invalid", ("value", Echo(id)));

    private static Message SeasonInvalidError(string season) =>
        Message.Of("library-links.set.season-invalid", ("value", Echo(season)), ("max", MaximumSeasonNumber));

    private static Message EpisodeInvalidError(string episode) =>
        Message.Of("library-links.set.episode-invalid", ("value", Echo(episode)), ("max", MaximumEpisodeNumber));

    private static Message UnknownItemError(string itemId) => Message.Of("library-links.set.unknown-item", ("value", Echo(itemId)));

    // A key per kind: "film" and "show" are words of the sentence, which a translation inflects (MAM-47, ADR 0042).
    internal static Message CatalogWorkNotFoundError(MediaWorkKind kind, string id) =>
        kind == MediaWorkKind.Series
            ? Message.Of("library-links.catalog.show-not-found", ("id", id))
            : Message.Of("library-links.catalog.film-not-found", ("id", id));

    public async Task<LibraryLinkResult> SetAsync(Guid workId, LibraryLinkRequest? request, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var (pick, error) = Parse(request ?? new LibraryLinkRequest());
        if (pick is null)
            return LibraryLinkResult.Invalid(error!);

        var work = await db.MediaWorks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == workId, cancellationToken);
        if (work is null)
            return LibraryLinkResult.NotFound;
        var (target, targetError) = await TargetOfAsync(work, pick, cancellationToken);
        if (target is null)
            return LibraryLinkResult.Invalid(targetError!);

        var items = await ItemsOfAsync(work.Id, cancellationToken);
        if (items.Count == 0)
            return LibraryLinkResult.Conflict(NoItemsError);
        // R17 — the items the owner ticked; every item of the work when none is named.
        if (pick.SourceItemIds?.FirstOrDefault(itemId => items.All(item => item.SourceItemId != itemId)) is { } unknownItem)
            return LibraryLinkResult.Invalid(UnknownItemError(unknownItem));

        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
            return LibraryLinkResult.Conflict(connected.Error!);
        var (catalogWork, catalogFailure) = await CatalogWorkAsync(catalog, pick, cancellationToken);
        if (catalogWork is null)
            return catalogFailure!;
        // MAM-34 — an episode's link is identified by the episode's own IMDb id: asked of TMDb now, so the save knows whether
        // the link is queued or stays in the app, and why.
        var episode = await EpisodeImdbOfPickAsync(work, pick, target.Value, items, catalog, cancellationToken);
        if (episode?.Error is { } episodeError)
            return LibraryLinkResult.BadGateway(episodeError);

        var actor = await ActorAsync(ownerUserId, cancellationToken);
        var saved = await SaveLinksAsync(work, pick, target.Value, catalog, catalogWork, episode, actor, batchId: null, cancellationToken);
        if (saved.Failure is { } failure)
            return failure;

        // Saved: the links count from here on, and the background writer identifies the queued ones (R9, ADR 0013); the
        // dialog shows them Queued and its poll follows them to the end.
        if (saved.Queued)
            writeQueue.Signal();
        if (saved.Changed)
        {
            logger.LogInformation("Work {WorkId} was linked to TMDb on {Count} library items.", work.Id, saved.LinkIds.Count);
            await QueueRecommendationsAsync(ownerUserId);
        }

        return await DialogAsync(work.Id, CancellationToken.None) is { } dialog ? LibraryLinkResult.Succeeded(dialog) : LibraryLinkResult.NotFound;
    }

    public async Task<LibraryLinkBatchResponse> LinkBatchAsync(IReadOnlyList<LibraryLinkBatchPick> picks, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(picks);

        var batchId = Guid.NewGuid();
        var actor = await ActorAsync(ownerUserId, cancellationToken);
        // One catalog for the click, found once.
        var connected = picks.Any(pick => pick.SkipReason is null) ? await catalogs.ConnectAsync(cancellationToken) : null;

        var outcomes = new List<(Guid WorkId, Message? SkipReason)>();
        try
        {
            foreach (var pick in picks)
            {
                if (pick.SkipReason is { } skipReason)
                {
                    outcomes.Add((pick.WorkId, skipReason));
                    continue;
                }

                var (saved, reason) = await LinkOneOfBatchAsync(pick, batchId, connected!, actor, cancellationToken);
                outcomes.Add((pick.WorkId, reason));
                // R13 — the worker woken as each work is saved, not once at the end: a click the browser leaves halfway
                // (its request cancelled) has its saved links identified all the same (R9). The signal is one slot that
                // drops the extras, and the worker takes every queued link it finds.
                if (saved?.Queued == true)
                    writeQueue.Signal();
            }
        }
        finally
        {
            // However the click ended, the works saved so far are linked: they count, and the recommendations are
            // computed again (QueueRecommendationsAsync never takes the request's token).
            var linked = outcomes.Count(outcome => outcome.SkipReason is null);
            logger.LogInformation("Accept all confident {BatchId}: {Linked} works linked, {Skipped} skipped.", batchId, linked, outcomes.Count - linked);
            if (linked > 0)
                await QueueRecommendationsAsync(ownerUserId);
        }

        var rows = (await ReadBatchAsync(batchId, CancellationToken.None))?.Items.ToDictionary(item => item.WorkId) ?? [];
        return new LibraryLinkBatchResponse(
            batchId,
            [
                .. outcomes.Select(outcome => outcome.SkipReason is null
                    ? new LibraryLinkBatchItemDto(outcome.WorkId, LinkBatchOutcome.Linked, null, rows.TryGetValue(outcome.WorkId, out var item) ? item.Links : [])
                    : new LibraryLinkBatchItemDto(outcome.WorkId, LinkBatchOutcome.Skipped, texts.Render(outcome.SkipReason), [])),
            ]);
    }

    // One work of the click, linked as the dialog's Save links it — the identifications left to the background writer — or
    // why it was skipped.
    private async Task<(SaveOutcome? Saved, Message? SkipReason)> LinkOneOfBatchAsync(
        LibraryLinkBatchPick batchPick, Guid batchId, CatalogConnectResult connected, AuditActor actor, CancellationToken cancellationToken)
    {
        var pick = new LinkPick(batchPick.CatalogKind, batchPick.CatalogId.Trim(), null, null, null);
        if (pick.CatalogKind is not (MediaWorkKind.Movie or MediaWorkKind.Series))
            return (null, KindInvalidError(pick.CatalogKind.ToString()));
        if (!MediaIdentifierRules.IsTmdbId(pick.CatalogId))
            return (null, IdInvalidError(pick.CatalogId));

        var work = await db.MediaWorks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == batchPick.WorkId, cancellationToken);
        if (work is null)
            return (null, WorkGoneError);
        var (target, targetError) = await TargetOfAsync(work, pick, cancellationToken);
        if (target is null)
            return (null, targetError);
        if (connected.Connection is not { } catalog)
            return (null, connected.Error);
        var (catalogWork, catalogFailure) = await CatalogWorkAsync(catalog, pick, cancellationToken);
        if (catalogWork is null)
            return (null, catalogFailure!.Error);

        // A click's picks name no season or episode, so no episode's IMDb id is asked.
        var saved = await SaveLinksAsync(work, pick, target.Value, catalog, catalogWork, episode: null, actor, batchId, cancellationToken);
        return saved.Failure switch
        {
            null => (saved, null),
            { Status: LibraryLinkStatus.NotFound } => (null, WorkGoneError),
            { Error: var error } => (null, error),
        };
    }

    // ── the request ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The request's fields parsed (R18: strings, so every mistake gets its sentence), or why not.</summary>
    private static (LinkPick? Pick, Message? Error) Parse(LibraryLinkRequest request)
    {
        var kindText = request.CatalogKind?.Trim();
        if (string.IsNullOrEmpty(kindText))
            return (null, KindMissingError);
        MediaWorkKind kind;
        if (string.Equals(kindText, nameof(MediaWorkKind.Movie), StringComparison.OrdinalIgnoreCase))
            kind = MediaWorkKind.Movie;
        else if (string.Equals(kindText, nameof(MediaWorkKind.Series), StringComparison.OrdinalIgnoreCase))
            kind = MediaWorkKind.Series;
        else
            return (null, KindInvalidError(kindText));

        var id = request.CatalogId?.Trim();
        if (string.IsNullOrEmpty(id))
            return (null, IdMissingError);
        if (!MediaIdentifierRules.IsTmdbId(id))
            return (null, IdInvalidError(id));

        int? season = null;
        if (!string.IsNullOrWhiteSpace(request.SeasonNumber))
        {
            var text = request.SeasonNumber.Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number > MaximumSeasonNumber)
                return (null, SeasonInvalidError(text));
            season = number;
        }

        int? episode = null;
        if (!string.IsNullOrWhiteSpace(request.EpisodeNumber))
        {
            var text = request.EpisodeNumber.Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > MaximumEpisodeNumber)
                return (null, EpisodeInvalidError(text));
            episode = number;
        }

        if (episode is not null && season is null)
            return (null, EpisodeWithoutSeasonError);

        IReadOnlyList<string>? itemIds = request.SourceItemIds is { Count: > 0 } ids ? [.. ids.Select(itemId => itemId?.Trim() ?? "")] : null;
        return (new LinkPick(kind, id, season, episode, itemIds), null);
    }

    /// <summary>
    /// What the link says the work's items are, by the work's kind and the catalog's: a film is a film (Movie work ↔
    /// TMDb film), a show is a show (Series work ↔ TMDb show), and a show picked for a film — or for an episode no series
    /// holds — makes it an episode of that show. Why not when the pair can't be linked.
    /// </summary>
    private async Task<(LibraryLinkTarget? Target, Message? Error)> TargetOfAsync(MediaWorkEntity work, LinkPick pick, CancellationToken cancellationToken)
    {
        LibraryLinkTarget target;
        switch (work.Kind)
        {
            case MediaWorkKind.Movie:
                target = pick.CatalogKind == MediaWorkKind.Movie ? LibraryLinkTarget.Work : LibraryLinkTarget.ShowEpisode;
                break;
            case MediaWorkKind.Series:
                if (pick.CatalogKind != MediaWorkKind.Series)
                    return (null, FilmForShowError);
                target = LibraryLinkTarget.Work;
                break;
            default:
                // Decision 15 — an episode of a series is known by its series' ids.
                if (await db.MediaEpisodePositions.AnyAsync(position => position.MediaWorkId == work.Id && position.SeriesWorkId != null, cancellationToken))
                    return (null, SeriesEpisodeError);
                if (pick.CatalogKind != MediaWorkKind.Series)
                    return (null, FilmForShowError);
                target = LibraryLinkTarget.ShowEpisode;
                break;
        }

        if (target != LibraryLinkTarget.ShowEpisode && (pick.SeasonNumber is not null || pick.EpisodeNumber is not null))
            return (null, NumbersNotAllowedError);
        return (target, null);
    }

    /// <summary>The catalog's film or show (its title and IMDb id are copied onto the link; it is stored, so its poster
    /// serves), or the result saying why not.</summary>
    private async Task<(CatalogWork? Work, LibraryLinkResult? Failure)> CatalogWorkAsync(CatalogConnection catalog, LinkPick pick, CancellationToken cancellationToken)
    {
        var read = await catalogStore.GetWorkAsync(catalog, pick.CatalogKind, pick.CatalogId, cancellationToken);
        if (read.IsSuccess)
            return (read.Value!, null);
        // R18 — an id the catalog doesn't know is the request's mistake, said in a sentence (DownloadsController's body ids).
        return read.Status == IntegrationCallStatus.NotFound
            ? (null, LibraryLinkResult.CatalogWorkNotFound(CatalogWorkNotFoundError(pick.CatalogKind, pick.CatalogId)))
            : (null, LibraryLinkResult.BadGateway(read.Error!.Redacted(catalog.Settings.Secrets)));
    }

    /// <summary>
    /// MAM-34 — what TMDb says of the IMDb id of the episode an episode's link names, when the link is identified by one: its
    /// season and episode named, on a film item whose library identifies it (Vasyl's decision of 2026-10-06). <c>null</c>
    /// when no item of the pick would be identified by it, so TMDb isn't asked.
    /// </summary>
    private async Task<LibraryLinkEpisodeImdb?> EpisodeImdbOfPickAsync(
        MediaWorkEntity work, LinkPick pick, LibraryLinkTarget target, IReadOnlyList<WorkItem> items, CatalogConnection catalog, CancellationToken cancellationToken)
    {
        if (target != LibraryLinkTarget.ShowEpisode || LibraryLinkWritability.LinkReason(target, pick.SeasonNumber, pick.EpisodeNumber) is not null)
            return null;
        var writes = items
            .Where(item => pick.SourceItemIds?.Contains(item.SourceItemId) ?? true)
            .Any(item => LibraryLinkWritability.ItemReason(adapters.Find<IMediaLibraryItemIdentifier>(item.Connection.AdapterType), item.Connection.Name, work.Kind) is null);
        return writes
            ? await LibraryLinkEpisodeImdb.AskAsync(catalog, pick.CatalogId, pick.SeasonNumber!.Value, pick.EpisodeNumber!.Value, cancellationToken)
            : null;
    }

    // ── saving ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ticket's step 1: per item the link created or replaced, with its audit row, in one transaction. R9 — the
    /// work, its items, their library ids and the links are read again inside it, so what the earlier reads saw can't
    /// have changed under the save: a work gone meanwhile is a bare 404, a link being identified (or a double click's twin)
    /// a 409. Each link identified by rule is saved <c>Queued</c>; the caller wakes the background writer.
    /// </summary>
    /// <param name="episode">MAM-34 — what TMDb said of the IMDb id of the episode an episode's link names; <c>null</c>
    /// when it wasn't asked (no item would be identified by it).</param>
    /// <param name="batchId">The click that queues the links — Accept all — or <c>null</c> for the owner's own Save in the
    /// dialog: what makes the identification protect the library's own ids or not (<c>BatchId</c>).</param>
    private async Task<SaveOutcome> SaveLinksAsync(
        MediaWorkEntity work,
        LinkPick pick,
        LibraryLinkTarget target,
        CatalogConnection catalog,
        CatalogWork catalogWork,
        LibraryLinkEpisodeImdb? episode,
        AuditActor actor,
        Guid? batchId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.MediaWorks.AnyAsync(row => row.Id == work.Id, cancellationToken))
                return SaveOutcome.Failed(LibraryLinkResult.NotFound);
            var items = await ItemsOfAsync(work.Id, cancellationToken);
            var selected = items.Where(item => pick.SourceItemIds?.Contains(item.SourceItemId) ?? true).ToList();
            if (selected.Count == 0)
                return SaveOutcome.Failed(LibraryLinkResult.Conflict(NoItemsError));
            var links = await LinksOfAsync(work.Id, items, tracking: true, cancellationToken);
            if (links.Any(link => link.WriteState == LibraryLinkWriteState.Writing))
                return SaveOutcome.Failed(LibraryLinkResult.Conflict(UnderWayError));
            // "Accept all confident" links works with no link: one linked since the suggestion was shown is the owner's.
            if (batchId is not null && links.Count > 0)
                return SaveOutcome.Failed(LibraryLinkResult.Conflict(LinkedMeanwhileError));

            var rows = await LibraryRowsOfAsync(work.Id, cancellationToken);
            var identities = await FileIdentitiesAsync(work, items, cancellationToken);
            List<Guid> linkIds = [.. links.Select(link => link.Id)];
            var held = (await db.LibraryItemLinkFiles
                    .AsNoTracking()
                    .Where(file => linkIds.Contains(file.LibraryItemLinkId))
                    .ToListAsync(cancellationToken))
                .ToLookup(file => file.LibraryItemLinkId, file => new FileIdentity(file.DeviceId, file.Inode, file.SizeBytes));
            var workTitle = TitleOf(await db.MediaWorkTitles.AsNoTracking().Where(title => title.MediaWorkId == work.Id).ToListAsync(cancellationToken));

            var scheme = pick.CatalogKind == MediaWorkKind.Series ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb;
            var value = catalogWork.Summary.Id;
            var imdbId = catalogWork.ImdbId?.Trim() is { } imdb && MediaIdentifierRules.IsImdbId(imdb) ? imdb : null;
            var now = time.GetUtcNow().UtcDateTime;
            var saved = new List<Guid>();
            var queued = false;
            foreach (var item in selected)
            {
                var existing = links.FirstOrDefault(item.Holds);
                if (existing is not null
                    && existing.Target == target
                    && existing.Scheme == scheme
                    && string.Equals(existing.Value, value, StringComparison.OrdinalIgnoreCase)
                    && existing.SeasonNumber == pick.SeasonNumber
                    && existing.EpisodeNumber == pick.EpisodeNumber
                    // MAM-34 — unless TMDb now gives the episode the IMDb id it gave none of when the link was saved: only
                    // asking TMDb tells it, and nothing else asks again (Retry and the catch-up leave such a link alone),
                    // so the same pick saved again writes it.
                    && !(LibraryLinkWritability.KnowsNoEpisodeImdb(existing) && episode?.ImdbId is not null))
                    continue;

                var library = rows[(item.Connection.Id, item.SourceItemId)].Select(row => new MediaIdentifier(row.Scheme, row.Value)).ToList();
                // Only an item of a kind its library identifies is ever identified (Jellyfin: a film or a series, never an
                // episode item): a link on any other stays in the app.
                var writesKind = LibraryLinkWritability.ItemReason(
                    adapters.Find<IMediaLibraryItemIdentifier>(item.Connection.AdapterType), item.Connection.Name, work.Kind) is null;
                // The id the item is identified by: a link to the work itself, or an episode's link with its numbers whose
                // episode TMDb gives an IMDb id (Vasyl's decision of 2026-10-06); any other is kept in the app.
                var keptReason = !writesKind
                    ? null
                    : LibraryLinkWritability.LinkReason(target, pick.SeasonNumber, pick.EpisodeNumber)
                        ?? (target == LibraryLinkTarget.ShowEpisode && episode?.ImdbId is null ? LibraryLinkWritability.NoEpisodeImdbReason : null);
                var writesIds = writesKind && keptReason is null;
                var (state, error) = writesIds
                    ? item.Connection.IsEnabled
                        // Always queued, the dialog's Save too: an Apply is the background writer's alone.
                        ? (LibraryLinkWriteState.Queued, (Message?)null)
                        // R12 — a library switched off is never identified in: the link waits for it.
                        : (LibraryLinkWriteState.NotWritten, LibraryLinkWriter.SwitchedOffError(item.Connection.Name))
                    // Kept in the app. TMDb's "no IMDb id of this episode" is the link's error, as only asking TMDb tells
                    // it: the writer and the repair don't ask again.
                    : (LibraryLinkWriteState.KeptInApp, keptReason?.Key == LibraryLinkWritability.NoEpisodeImdbKey ? keptReason : null);

                var link = existing ?? new LibraryItemLinkEntity
                {
                    IntegrationConnectionId = item.Connection.Id,
                    SourceItemId = item.SourceItemId,
                    CreatedUtc = now,
                };
                link.SupersededValue = SupersededValue(existing, target, scheme, value, library);
                link.ItemKind = work.Kind;
                link.MediaWorkId = work.Id;
                link.Target = target;
                link.Scheme = scheme;
                link.Value = value;
                link.SeasonNumber = pick.SeasonNumber;
                link.EpisodeNumber = pick.EpisodeNumber;
                link.CatalogTitle = Cut(catalogWork.Summary.Title, LibraryItemLinkEntity.TitleMaximumLength);
                link.ItemTitle = item.Title is { } itemTitle ? Cut(itemTitle, LibraryItemLinkEntity.TitleMaximumLength) : link.ItemTitle;
                link.ImdbId = imdbId;
                link.EpisodeImdbId = target == LibraryLinkTarget.ShowEpisode ? episode?.ImdbId : null;
                link.ImdbSourceConnectionId = imdbId is null ? null : catalog.IntegrationId;
                link.ImdbSourceName = imdbId is null ? null : catalog.Name;
                link.ImdbObservedUtc = imdbId is null ? null : now;
                link.SetByUserId = actor.UserId;
                link.SetByUserName = actor.UserName;
                link.SetUtc = now;
                link.BatchId = batchId;
                link.WriteState = state;
                link.WriteAttemptId = Guid.NewGuid();
                link.WriteError = error;
                link.WriteStateUtc = now;
                // A link set or replaced is not identified as its new id, whatever the one it replaces was: the repair counts
                // it until an identification confirms it. (The Lookup columns are dormant, ADR 0026: never touched.)
                link.IdentifiedUtc = null;
                if (existing is null)
                    db.LibraryItemLinks.Add(link);

                // Decision 10 — the identities of the item's files the inspection read: how the link finds its item again.
                foreach (var identity in identities(item).Where(identity => !held[link.Id].Contains(identity)))
                {
                    db.LibraryItemLinkFiles.Add(new LibraryItemLinkFileEntity
                    {
                        LibraryItemLinkId = link.Id,
                        DeviceId = identity.DeviceId,
                        Inode = identity.Inode,
                        SizeBytes = identity.SizeBytes,
                    });
                }

                // The ticket's audit row: the ids the item's library held when the link was set, and the link's.
                List<MediaIdentifier> after = [new(scheme, value)];
                if (imdbId is not null)
                    after.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));
                auditTrail.Record(
                    existing is null ? AuditAction.LibraryLinkSet : AuditAction.LibraryLinkReplaced,
                    actor,
                    AuditTarget.LibraryLink(link.Id, workTitle ?? link.ItemTitle ?? item.SourceItemId),
                    LibraryLinkKeys.AuditIdentifiers(
                        library.Where(identifier => identifier.Scheme is MediaIdentifierScheme.Imdb or MediaIdentifierScheme.Tmdb or MediaIdentifierScheme.TmdbTv),
                        after));

                queued |= state == LibraryLinkWriteState.Queued;
                saved.Add(link.Id);
            }

            // MAM-4's rule: a save that changes nothing records nothing (and, here, writes nothing).
            if (saved.Count == 0)
                return new SaveOutcome(null, [], Changed: false, Queued: false);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(CancellationToken.None);
            return new SaveOutcome(null, saved, Changed: true, queued);
        }
        catch (DbUpdateException)
        {
            // R9 — the one-link-per-item index refused a twin (a double click whose other half saved first): its links
            // are there now, being identified.
            db.ChangeTracker.Clear();
            return SaveOutcome.Failed(await db.MediaWorks.AnyAsync(row => row.Id == work.Id, CancellationToken.None)
                ? LibraryLinkResult.Conflict(UnderWayError)
                : LibraryLinkResult.NotFound);
        }
        finally
        {
            // Nothing stays tracked for the reads that follow on this context.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The library's own id the link replaces (R6, R7), which readers and the grouping drop while the link isn't
    /// confirmed in the library: for a link to the work itself, the TMDb id the library holds — or, replacing a link the
    /// library holds, that link's value — when it differs from the new one; for an episode's link to its show, the
    /// film id the library holds for the item, which the episode's Identify drops (recorded live).
    /// </summary>
    private static string? SupersededValue(
        LibraryItemLinkEntity? existing,
        LibraryLinkTarget target,
        MediaIdentifierScheme scheme,
        string value,
        IReadOnlyList<MediaIdentifier> library)
    {
        if (target == LibraryLinkTarget.ShowEpisode)
            return library.FirstOrDefault(identifier => identifier.Scheme == MediaIdentifierScheme.Tmdb)?.Value;
        if (existing is { Target: LibraryLinkTarget.Work, WriteState: LibraryLinkWriteState.Written or LibraryLinkWriteState.InLibrary }
            && existing.Scheme == scheme
            && !string.Equals(existing.Value.Trim(), value, StringComparison.OrdinalIgnoreCase))
            return existing.Value;
        return library.FirstOrDefault(identifier => identifier.Scheme == scheme) is { } held
            && !string.Equals(held.Value.Trim(), value, StringComparison.OrdinalIgnoreCase)
            ? held.Value
            : null;
    }

    /// <summary>
    /// The identities (device, inode and size, R2) of the work's files an inspection read, per item: a film's or an
    /// episode's own; a series' its episodes' (R3), as a series item has no files.
    /// </summary>
    /// <remarks>
    /// A series' episodes are known here by the series work they sit under, not by their series item: when the work holds
    /// several series items of one library (the Penguins' two folders), which episodes are whose is unknown, so none of
    /// them gets any. Each would otherwise hold the others' episodes, and could later move onto an item none of its files
    /// was under (decision 10). The next sync gives each its own, as it knows every episode's series item.
    /// </remarks>
    private async Task<Func<WorkItem, IReadOnlyList<FileIdentity>>> FileIdentitiesAsync(MediaWorkEntity work, IReadOnlyList<WorkItem> items, CancellationToken cancellationToken)
    {
        var isSeries = work.Kind == MediaWorkKind.Series;
        var workIds = isSeries
            ? db.MediaEpisodePositions.Where(position => position.SeriesWorkId == work.Id).Select(position => position.MediaWorkId)
            : db.MediaWorks.Where(row => row.Id == work.Id).Select(row => row.Id);
        var files = await (
                from version in db.MediaVersions.AsNoTracking()
                join file in db.PhysicalFiles.AsNoTracking() on (Guid?)version.Id equals file.MediaVersionId
                where workIds.Contains(version.MediaWorkId)
                    && file.SourceKind == ProvenanceSourceKind.MediaLibrary
                    && file.SourceConnectionId != null
                    && file.SourceItemId != null
                    && file.DeviceId != null
                    && file.Inode != null
                    && file.ReadSizeBytes != null
                select new
                {
                    ConnectionId = file.SourceConnectionId!.Value,
                    ItemId = file.SourceItemId!,
                    Identity = new FileIdentity(file.DeviceId!.Value, file.Inode!.Value, file.ReadSizeBytes!.Value),
                })
            .ToListAsync(cancellationToken);
        return item =>
            isSeries && items.Count(other => other.Connection.Id == item.Connection.Id) != 1
                ? []
                :
                [
                    .. files
                        .Where(file => file.ConnectionId == item.Connection.Id && (isSeries || file.ItemId == item.SourceItemId))
                        .Select(file => file.Identity)
                        .Distinct(),
                ];
    }

    private static string Cut(string text, int maximumLength) => FileInspectionRunUpdates.Cut(text, maximumLength);

    /// <summary>What the owner picked, parsed.</summary>
    private sealed record LinkPick(MediaWorkKind CatalogKind, string CatalogId, int? SeasonNumber, int? EpisodeNumber, IReadOnlyList<string>? SourceItemIds);

    /// <summary>A file's identity as the inspection read it (R2).</summary>
    private readonly record struct FileIdentity(long DeviceId, long Inode, long SizeBytes);

    /// <summary>What saving the links did: the failure, or the links saved and whether any was queued for the background
    /// writer.</summary>
    private sealed record SaveOutcome(LibraryLinkResult? Failure, IReadOnlyList<Guid> LinkIds, bool Changed, bool Queued)
    {
        public static SaveOutcome Failed(LibraryLinkResult failure) => new(failure, [], Changed: false, Queued: false);
    }
}
