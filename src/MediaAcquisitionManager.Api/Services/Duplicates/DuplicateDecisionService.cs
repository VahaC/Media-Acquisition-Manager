using System.Globalization;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <inheritdoc cref="IDuplicateDecisionService"/>
/// <remarks>
/// The group a key names is worked out as the list does, on every call (<see cref="ILibraryDuplicateService.FindAsync"/>):
/// a decision is only ever given to a group that is on the list now, never to a key the caller made up or one a sync has
/// since taken apart. Which class takes which decision, and when a decision on kept copies holds, is
/// <see cref="DuplicateDecisionRules"/>' — the list reads the same rules.
/// </remarks>
public sealed class DuplicateDecisionService(
    ApplicationDbContext db,
    ILibraryDuplicateService duplicates,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<DuplicateDecisionService> logger) : IDuplicateDecisionService
{
    /// <summary>
    /// The sentence a body that can't be read as a decision gets — not JSON, or a decision that isn't text, such as
    /// <c>1</c> — instead of the validation body MVC would send: it says what the body must be.
    /// </summary>
    public const string UnreadableBodyError = "The body must be JSON with a decision, such as { \"decision\": \"Confirmed\" }.";

    public async Task<DuplicateDecisionResult> DecideAsync(
        string key, string? decision, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        if (DuplicateDecisionRules.Validate(decision, out var matchDecision) is { } error)
            return DuplicateDecisionResult.Invalid(error);
        var found = await GroupAsync(key, matchDecision, cancellationToken);
        if (found.Refusal is { } refusal)
            return refusal;

        var group = found.Group!;
        var name = await NameOfAsync(group, cancellationToken);
        var actor = await ActorAsync(ownerUserId, cancellationToken);
        try
        {
            await ApplyAsync(group, matchDecision, actor, name, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two first decisions can both find no row; the unique index lets one commit, and this one lost. It is given
            // once more, to the row that won, as a decision given just after it: the last click counts, and its audit row
            // says so (none when both said the same). Anything else (a full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.DuplicateMatchDecisions.AnyAsync(row => row.GroupKey == key, cancellationToken))
                throw;
            logger.LogInformation(exception, "A concurrent request decided duplicate group {GroupKey} first.", key);
            await ApplyAsync(group, matchDecision, actor, name, cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return DuplicateDecisionResult.Done;
    }

    public async Task<DuplicateDecisionResult> UndoAsync(string key, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var found = await GroupAsync(key, decision: null, cancellationToken);
        if (found.Refusal is { } refusal)
            return refusal;

        var group = found.Group!;
        var row = await db.DuplicateMatchDecisions.AsNoTracking().SingleOrDefaultAsync(existing => existing.GroupKey == key, cancellationToken);
        // Nothing to undo — never decided, or MAM-40 kept copies a file has joined since, which the list shows undecided:
        // that row stays, ignored, and nothing is recorded.
        if (row is null || !DuplicateDecisionRules.IsInForce(group, row.KeptMembers))
            return DuplicateDecisionResult.Done;

        var name = await NameOfAsync(group, cancellationToken);
        var actor = await ActorAsync(ownerUserId, cancellationToken);
        // Never cancelled once started: the delete and its audit row go together or not at all (the link removal's rule).
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var deleted = await db.DuplicateMatchDecisions
            .Where(existing => existing.GroupKey == key)
            .ExecuteDeleteAsync(CancellationToken.None);
        // Another request's undo forgot it first: nothing is recorded.
        if (deleted == 0)
            return DuplicateDecisionResult.Done;

        auditTrail.Record(
            group.Class == DuplicateClass.Possible ? AuditAction.DuplicateMatchDecisionUndone : AuditAction.DuplicateCopiesKeptUndone,
            actor,
            AuditTarget.DuplicateMatch(key, name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        logger.LogInformation("The decision on duplicate group {GroupKey} ({Class}) was undone by user {UserId}.", key, group.Class, actor.UserId);
        return DuplicateDecisionResult.Done;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The group <paramref name="key"/> names now, when it takes <paramref name="decision"/> (an undo, when
    /// <c>null</c>), or the result refusing the key: a bare not-found for a key no group could have, without reading the
    /// library, or none has now; the sentence for a class that never takes it (<see cref="DuplicateDecisionRules.RefusalOf"/>).
    /// </summary>
    private async Task<(DuplicateDecisionResult? Refusal, DuplicateGroup? Group)> GroupAsync(
        string key, DuplicateMatchDecision? decision, CancellationToken cancellationToken)
    {
        if (!DuplicateDecisionRules.IsGroupKey(key))
            return (DuplicateDecisionResult.NotFound, null);
        if (await duplicates.FindAsync(key, cancellationToken) is not { } group)
            return (DuplicateDecisionResult.NotFound, null);
        if (DuplicateDecisionRules.RefusalOf(group.Class, decision) is { } refusal)
            return (DuplicateDecisionResult.Invalid(refusal), null);

        return (null, group);
    }

    /// <summary>
    /// Gives <paramref name="group"/> <paramref name="decision"/> — its row added on the group's first decision, changed
    /// after — with its audit row, in one save; nothing at all when the row already says so and holds, so a double click
    /// or a retry records nothing twice.
    /// </summary>
    /// <remarks>
    /// MAM-40 — kept copies store the members kept (<see cref="DuplicateDecisionRules.KeptMembersOf"/>). A kept group a
    /// file has joined since is undecided on the list, so keeping it again is a decision of its own: the row takes the
    /// members it has now, and the audit row is written.
    /// </remarks>
    private async Task ApplyAsync(
        DuplicateGroup group, DuplicateMatchDecision decision, AuditActor actor, string name, CancellationToken cancellationToken)
    {
        var key = group.Key;
        var row = await db.DuplicateMatchDecisions.SingleOrDefaultAsync(existing => existing.GroupKey == key, cancellationToken);
        if (row is not null && row.Decision == decision && DuplicateDecisionRules.IsInForce(group, row.KeptMembers))
            return;

        var now = time.GetUtcNow().UtcDateTime;
        if (row is null)
        {
            row = new DuplicateMatchDecisionEntity { GroupKey = key, CreatedUtc = now };
            db.DuplicateMatchDecisions.Add(row);
        }

        row.Decision = decision;
        row.KeptMembers = DuplicateDecisionRules.KeptMembersOf(group);
        row.DecidedByUserId = actor.UserId;
        row.DecidedByUserName = actor.UserName;
        row.DecidedUtc = now;
        // A change of mind records the decision it changed to: Confirmed after Dismissed is a confirmation. Copies are only
        // ever kept (DuplicateDecisionRules.RefusalOf).
        var action = group.Class != DuplicateClass.Possible
            ? AuditAction.DuplicateCopiesKept
            : decision == DuplicateMatchDecision.Confirmed ? AuditAction.DuplicateMatchConfirmed : AuditAction.DuplicateMatchDismissed;
        auditTrail.Record(action, actor, AuditTarget.DuplicateMatch(key, name));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Duplicate group {GroupKey} ({Class}) {Decision} by user {UserId}.", key, group.Class, decision, actor.UserId);
    }

    /// <summary>
    /// The name a group's audit rows carry: a possible match by the first title and year it is offered by, "Gone (2001)";
    /// MAM-40 — kept copies, which have no such title, by their first work as its card heads it, "Alien (1979)", an
    /// episode by its series' title and its place, "Clarkson's Farm S02E03"; copies no library reports by the first one's
    /// file name; the key should none of those be there.
    /// </summary>
    private async Task<string> NameOfAsync(DuplicateGroup group, CancellationToken cancellationToken)
    {
        if (group.Titles is [var title, ..])
            return string.Create(CultureInfo.InvariantCulture, $"{title.Text} ({title.Year})");
        if (await FirstWorkNameAsync(group.WorkIds, cancellationToken) is { } workName)
            return workName;
        return group.MemberPathKeys is [var path, ..] ? ReplacementText.FileNameOf(path) : group.Key;
    }

    /// <summary>
    /// The first of <paramref name="workIds"/>' names in title order (<see cref="LibraryTitleOrder"/>, then ordinal), each
    /// named as its card is (<see cref="LibraryDuplicateMapper"/>): three queries for the group, whatever its size.
    /// <c>null</c> when none has a title.
    /// </summary>
    private async Task<string?> FirstWorkNameAsync(IReadOnlyList<Guid> workIds, CancellationToken cancellationToken)
    {
        if (workIds.Count == 0)
            return null;

        List<Guid> ids = [.. workIds];
        var kinds = await db.MediaWorks.AsNoTracking()
            .Where(work => EF.Parameter(ids).Contains(work.Id))
            .ToDictionaryAsync(work => work.Id, work => work.Kind, cancellationToken);
        var positions = (await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => EF.Parameter(ids).Contains(position.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(position => position.MediaWorkId);
        List<Guid> titled = [.. ids.Concat(positions.SelectMany(work => work).Select(position => position.SeriesWorkId).OfType<Guid>()).Distinct()];
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => EF.Parameter(titled).Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);

        return kinds
            .Select(work => NameOf(work.Key, work.Value, titles, positions))
            .OfType<string>()
            .OrderBy(workName => workName, LibraryTitleOrder.Comparer)
            .ThenBy(workName => workName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // A work as its card heads it: an episode at a place of a titled series by the series' title and the place, else the
    // card's title — a displayed one, else an original one — with its year when known.
    private static string? NameOf(
        Guid workId, MediaWorkKind kind, ILookup<Guid, MediaWorkTitleEntity> titles, ILookup<Guid, MediaEpisodePositionEntity> positions)
    {
        if (kind == MediaWorkKind.Episode
            && LibraryMapper.ToEpisodeDto([.. positions[workId]]) is { SeriesId: { } seriesId, SeasonNumber: { } season, EpisodeNumber: { } episode }
            && CardTitleOf([.. titles[seriesId]]) is { } series)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{series.Text} S{season:00}E{episode:00}");
        }

        IReadOnlyList<MediaWorkTitleEntity> own = [.. titles[workId]];
        var display = LibraryMapper.FirstInTitleOrder(own, MediaTitleKind.Display);
        var original = LibraryMapper.FirstInTitleOrder(own, MediaTitleKind.Original);
        if ((display ?? original)?.Text is not { } text)
            return null;
        return (display?.Year ?? original?.Year) is { } year ? string.Create(CultureInfo.InvariantCulture, $"{text} ({year})") : text;
    }

    private static MediaWorkTitleEntity? CardTitleOf(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display) ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original);

    private Task<AuditActor> ActorAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(user => user.Id == userId).Select(user => new AuditActor(user.Id, user.UserName)).SingleAsync(cancellationToken);
}
