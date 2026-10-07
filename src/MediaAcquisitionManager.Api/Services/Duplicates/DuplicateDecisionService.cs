using System.Globalization;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <inheritdoc cref="IDuplicateDecisionService"/>
/// <remarks>
/// The group a key names is worked out as the list does, on every call (<see cref="ILibraryDuplicateService.FindAsync"/>):
/// a decision is only ever given to a possible match that is on the list now, never to a key the caller made up or one
/// a sync has since taken apart.
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
        var possibleMatch = await PossibleMatchAsync(key, cancellationToken);
        if (possibleMatch.Refusal is { } refusal)
            return refusal;

        var actor = await ActorAsync(ownerUserId, cancellationToken);
        try
        {
            await ApplyAsync(key, matchDecision, actor, possibleMatch.Name, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two first decisions can both find no row; the unique index lets one commit, and this one lost. It is given
            // once more, to the row that won, as a decision given just after it: the last click counts, and its audit row
            // says so (none when both said the same). Anything else (a full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.DuplicateMatchDecisions.AnyAsync(row => row.GroupKey == key, cancellationToken))
                throw;
            logger.LogInformation(exception, "A concurrent request decided possible match {GroupKey} first.", key);
            await ApplyAsync(key, matchDecision, actor, possibleMatch.Name, cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return DuplicateDecisionResult.Done;
    }

    public async Task<DuplicateDecisionResult> UndoAsync(string key, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var possibleMatch = await PossibleMatchAsync(key, cancellationToken);
        if (possibleMatch.Refusal is { } refusal)
            return refusal;

        var actor = await ActorAsync(ownerUserId, cancellationToken);
        // Never cancelled once started: the delete and its audit row go together or not at all (the link removal's rule).
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var deleted = await db.DuplicateMatchDecisions
            .Where(row => row.GroupKey == key)
            .ExecuteDeleteAsync(CancellationToken.None);
        // Nothing to undo — never decided, or another request's undo forgot it first: nothing is recorded.
        if (deleted == 0)
            return DuplicateDecisionResult.Done;

        auditTrail.Record(AuditAction.DuplicateMatchDecisionUndone, actor, AuditTarget.DuplicateMatch(key, possibleMatch.Name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        logger.LogInformation("The decision on possible match {GroupKey} was undone by user {UserId}.", key, actor.UserId);
        return DuplicateDecisionResult.Done;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The possible match <paramref name="key"/> names now, by the name its audit rows carry — the first title and year it
    /// is offered by, "Gone (2001)" — or the result refusing the key (the name empty): a bare not-found for a key no group
    /// could have, without reading the library, or none has now; the sentence for a group of another class.
    /// </summary>
    private async Task<(DuplicateDecisionResult? Refusal, string Name)> PossibleMatchAsync(string key, CancellationToken cancellationToken)
    {
        if (!DuplicateDecisionRules.IsGroupKey(key))
            return (DuplicateDecisionResult.NotFound, "");
        if (await duplicates.FindAsync(key, cancellationToken) is not { } group)
            return (DuplicateDecisionResult.NotFound, "");
        if (group.Class != DuplicateClass.Possible)
            return (DuplicateDecisionResult.Invalid(DuplicateDecisionRules.NotAPossibleMatchError), "");

        // A possible match is offered by a title two of its subjects share, so it has one; the key should it ever not.
        return (null, group.Titles is [var title, ..] ? string.Create(CultureInfo.InvariantCulture, $"{title.Text} ({title.Year})") : key);
    }

    /// <summary>
    /// Gives <paramref name="key"/>'s group <paramref name="decision"/> — its row added on the group's first decision,
    /// changed after — with its audit row, in one save; nothing at all when the row already says so, so a double click or
    /// a retry records nothing twice.
    /// </summary>
    private async Task ApplyAsync(
        string key, DuplicateMatchDecision decision, AuditActor actor, string name, CancellationToken cancellationToken)
    {
        var row = await db.DuplicateMatchDecisions.SingleOrDefaultAsync(existing => existing.GroupKey == key, cancellationToken);
        if (row?.Decision == decision)
            return;

        var now = time.GetUtcNow().UtcDateTime;
        if (row is null)
        {
            row = new DuplicateMatchDecisionEntity { GroupKey = key, CreatedUtc = now };
            db.DuplicateMatchDecisions.Add(row);
        }

        row.Decision = decision;
        row.DecidedByUserId = actor.UserId;
        row.DecidedByUserName = actor.UserName;
        row.DecidedUtc = now;
        // A change of mind records the decision it changed to: Confirmed after Dismissed is a confirmation.
        auditTrail.Record(
            decision == DuplicateMatchDecision.Confirmed ? AuditAction.DuplicateMatchConfirmed : AuditAction.DuplicateMatchDismissed,
            actor,
            AuditTarget.DuplicateMatch(key, name));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Possible match {GroupKey} {Decision} by user {UserId}.", key, decision, actor.UserId);
    }

    private Task<AuditActor> ActorAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(user => user.Id == userId).Select(user => new AuditActor(user.Id, user.UserName)).SingleAsync(cancellationToken);
}
