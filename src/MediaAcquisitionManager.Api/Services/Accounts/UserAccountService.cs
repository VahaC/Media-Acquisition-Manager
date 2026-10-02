using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <inheritdoc cref="IUserAccountService"/>
public sealed class UserAccountService(
    ApplicationDbContext db,
    IPasswordHasher hasher,
    IAuditTrail auditTrail,
    ISessionService sessions,
    IUserMapper mapper,
    TimeProvider time,
    ILogger<UserAccountService> logger) : IUserAccountService
{
    private const string OwnerCannotBeDisabled = "The installation owner can't be disabled.";
    private const string OwnerCannotBeDeleted = "The installation owner can't be deleted.";
    private const string CurrentPasswordRequired = "Enter your current password.";
    private const string CurrentPasswordIncorrect = "The current password is incorrect.";

    public async Task<UserListResult> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return UserListResult.Invalid(pagingError);

        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matched against the normalised name, so the search ignores letter case the way sign-in
            // does. Contains becomes instr() on SQLite, which reads '%' and '_' as themselves: a
            // search is text, not a LIKE pattern.
            var normalizedSearch = UserNameRules.Normalize(search);
            users = users.Where(user => user.NormalizedUserName.Contains(normalizedSearch));
        }

        var totalCount = await users.CountAsync(cancellationToken);
        // Past the last page there is nothing to read; and a page far enough out would overflow the
        // offset, which EF Core takes as an int.
        var offset = (long)(page - 1) * pageSize;
        List<UserEntity> rows = offset >= totalCount
            ? []
            : await users
                // The normalised name is unique, so the order is total and no user shows on two pages.
                .OrderBy(user => user.NormalizedUserName)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        var ownerUserId = await db.Installations.Select(installation => (Guid?)installation.OwnerUserId).SingleOrDefaultAsync(cancellationToken);
        var items = rows.Select(user => mapper.ToDto(user, user.Id == ownerUserId)).ToList();
        return UserListResult.Listed(new UserListResponse(items, totalCount, page, pageSize));
    }

    public async Task<UserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : mapper.ToDto(user, await IsOwnerAsync(user.Id, cancellationToken));
    }

    public async Task<UserAccountResult> CreateAsync(string userName, string password, AccountCaller caller, CancellationToken cancellationToken = default)
    {
        if (UserNameRules.Validate(userName) is { } userNameError)
            return UserAccountResult.Invalid(userNameError);
        if (PasswordRules.Validate(password) is { } passwordError)
            return UserAccountResult.Invalid(passwordError);

        var trimmedUserName = userName.Trim();
        var normalizedUserName = UserNameRules.Normalize(trimmedUserName);
        if (await db.Users.AnyAsync(existing => existing.NormalizedUserName == normalizedUserName, cancellationToken))
            return NameTaken(trimmedUserName);

        var actor = await ActorAsync(caller, cancellationToken);
        var user = new UserEntity
        {
            UserName = trimmedUserName,
            NormalizedUserName = normalizedUserName,
            PasswordHash = hasher.Hash(password),
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Users.Add(user);
        auditTrail.Record(AuditAction.UserCreated, actor, AuditTarget.User(user));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // MAM-4 — two creates of one name can both pass the check above; the unique index on the
            // normalised name lets only one commit, and this one lost. Anything else (a full disk) is
            // not a conflict and stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.Users.AnyAsync(existing => existing.NormalizedUserName == normalizedUserName, cancellationToken))
                throw;

            logger.LogInformation(exception, "A concurrent request created a user named {UserName} first; this one changed nothing.", trimmedUserName);
            return NameTaken(trimmedUserName);
        }

        logger.LogInformation("{ActorUserName} created the user {UserName}.", actor.UserName, user.UserName);
        return UserAccountResult.Succeeded(mapper.ToDto(user, isOwner: false));
    }

    public async Task<UserAccountResult> DisableAsync(Guid userId, AccountCaller caller, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return UserAccountResult.NotFound;
        // MAM-4 — the owner is the one account that manages the others; disabling it would leave the
        // installation with nobody who can undo that.
        if (await IsOwnerAsync(user.Id, cancellationToken))
            return UserAccountResult.OwnerProtected(OwnerCannotBeDisabled);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Already disabled — by an earlier click, or by one racing this: nothing changes, so
            // nothing is recorded.
            if (await ChangeIsDisabledAsync(user.Id, isDisabled: true, cancellationToken))
            {
                var actor = await ActorAsync(caller, cancellationToken);
                auditTrail.Record(AuditAction.UserDisabled, actor, AuditTarget.User(user));
                await db.SaveChangesAsync(cancellationToken);
                // The session check already refuses a disabled user's tokens; ending the sessions too
                // means enabling the user later doesn't bring back a device signed in before.
                await sessions.RevokeAllForUserAsync(user.Id, cancellationToken: cancellationToken);
                logger.LogInformation("{ActorUserName} disabled the user {UserName}.", actor.UserName, user.UserName);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        user.IsDisabled = true;
        return UserAccountResult.Succeeded(mapper.ToDto(user, isOwner: false));
    }

    public async Task<UserAccountResult> EnableAsync(Guid userId, AccountCaller caller, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return UserAccountResult.NotFound;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Already enabled: nothing changes, so nothing is recorded. The sessions the disable
            // ended stay ended; the user signs in again.
            if (await ChangeIsDisabledAsync(user.Id, isDisabled: false, cancellationToken))
            {
                var actor = await ActorAsync(caller, cancellationToken);
                auditTrail.Record(AuditAction.UserEnabled, actor, AuditTarget.User(user));
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("{ActorUserName} enabled the user {UserName}.", actor.UserName, user.UserName);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        user.IsDisabled = false;
        return UserAccountResult.Succeeded(mapper.ToDto(user, await IsOwnerAsync(user.Id, cancellationToken)));
    }

    public async Task<UserAccountResult> ResetPasswordAsync(Guid userId, string password, AccountCaller caller, CancellationToken cancellationToken = default)
    {
        var user = await FindTrackedAsync(userId, cancellationToken);
        if (user is null)
            return UserAccountResult.NotFound;
        if (PasswordRules.Validate(password) is { } passwordError)
            return UserAccountResult.Invalid(passwordError);

        var actor = await ActorAsync(caller, cancellationToken);
        user.PasswordHash = hasher.Hash(password);
        auditTrail.Record(AuditAction.UserPasswordReset, actor, AuditTarget.User(user));
        // The session service revokes in a statement of its own; the transaction keeps it with the save,
        // so there is never a new password with the old sessions still running.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            // MAM-4 — a reset is how the owner locks someone out of an account whose password leaked,
            // so every session opened with the old one ends too. Except the caller's own: the owner may
            // reset their own password here, without giving the current one (MAM-16 added a change on
            // Profile that asks for it), and stays signed in on the device they did it from. For any
            // other user the caller's session isn't theirs anyway.
            await sessions.RevokeAllForUserAsync(user.Id, caller.SessionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("{ActorUserName} reset the password of {UserName}.", actor.UserName, user.UserName);
        return UserAccountResult.Succeeded();
    }

    public async Task<UserAccountResult> ChangeOwnPasswordAsync(AccountCaller caller, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        // Checked before anything is read or hashed: a blank field or a broken rule costs no PBKDF2 run.
        if (string.IsNullOrEmpty(currentPassword))
            return UserAccountResult.Invalid(CurrentPasswordRequired);
        if (PasswordRules.Validate(newPassword) is { } passwordError)
            return UserAccountResult.Invalid(passwordError);

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        if (user is null)
            return UserAccountResult.NotFound;

        // MAM-16 — a current password no rule would have let anyone set can't match, so it is refused
        // without the hash ever running over what someone posted (up to the request size limit).
        if (currentPassword.Length > PasswordRules.MaximumLength || !hasher.Verify(currentPassword, user.PasswordHash))
            return WrongCurrentPassword(user);

        // Hashed before the transaction: BEGIN IMMEDIATE takes SQLite's write lock, and holding it
        // through a PBKDF2 run (600,000 iterations in production) would stall every other write.
        var newPasswordHash = hasher.Hash(newPassword);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // MAM-16 — the check above ran on a copy read a PBKDF2 run ago. An owner's reset, a disable
            // or a delete that committed since must win, as it does over a sign-in (SessionService):
            // the reset is how the owner takes an account back from whoever knows its old password,
            // which is exactly who would be racing it here. So the change is claimed with a conditional
            // update that only matches the user as verified — same hash, still enabled, still there —
            // inside a transaction that holds the write lock until the other sessions are revoked.
            var changed = await db.Users
                .Where(u => u.Id == user.Id && u.PasswordHash == user.PasswordHash && !u.IsDisabled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.PasswordHash, newPasswordHash), cancellationToken) == 1;
            // Changed or gone since the check: the same refusal as a wrong password, never a 500. The
            // next request of a disabled or deleted user is refused by its session check anyway. The
            // log says what happened, though: the password matched, and an operator looking into an
            // owner's lockout shouldn't read a mismatch that never was.
            if (!changed)
            {
                logger.LogInformation("A password change by {UserName} was refused: the account changed while the current password was being checked.", user.UserName);
                return UserAccountResult.Invalid(CurrentPasswordIncorrect);
            }

            auditTrail.Record(AuditAction.UserPasswordChanged, AuditActor.From(user), AuditTarget.User(user));
            await db.SaveChangesAsync(cancellationToken);
            // ADR 0016 — whoever else knew the old password may be signed in somewhere; their sessions
            // end at once. The caller's own stays: they just proved who they are, and signing them out
            // of the page they are on would only make them type the new password straight away.
            await sessions.RevokeAllForUserAsync(user.Id, caller.SessionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("{UserName} changed their password.", user.UserName);
        return UserAccountResult.Succeeded();
    }

    public async Task<UserAccountResult> DeleteAsync(Guid userId, AccountCaller caller, CancellationToken cancellationToken = default)
    {
        var user = await FindTrackedAsync(userId, cancellationToken);
        if (user is null)
            return UserAccountResult.NotFound;
        // MAM-4 — like disabling, and the database's Restrict on the installation row refuses it too.
        if (await IsOwnerAsync(user.Id, cancellationToken))
            return UserAccountResult.OwnerProtected(OwnerCannotBeDeleted);

        var actor = await ActorAsync(caller, cancellationToken);
        // The audit row copies the name: it has to say whom it was about after the user is gone.
        auditTrail.Record(AuditAction.UserDeleted, actor, AuditTarget.User(user));
        // A tracked delete rather than ExecuteDelete, so it commits in the same save as its audit row.
        // The database does the rest: the user's sessions cascade, and the audit rows they acted in
        // keep their name with the actor id set to null.
        db.Users.Remove(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Deleted by another request since it was read (a double click): gone either way, and
            // that request recorded it. Nothing of this one was saved.
            db.ChangeTracker.Clear();
            return UserAccountResult.NotFound;
        }

        logger.LogInformation("{ActorUserName} deleted the user {UserName}.", actor.UserName, user.UserName);
        return UserAccountResult.Succeeded();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Tracked, so the change the caller makes to it is what the next save writes.
    private Task<UserEntity?> FindTrackedAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private Task<bool> IsOwnerAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Installations.AnyAsync(installation => installation.OwnerUserId == userId, cancellationToken);

    // Read from the database rather than the token: the audit row carries the name the account has.
    private Task<AuditActor> ActorAsync(AccountCaller caller, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == caller.UserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    private static UserAccountResult NameTaken(string userName) =>
        UserAccountResult.NameTaken($"A user named “{userName}” already exists.");

    // Information, not Warning: a mistyped password is routine, and the per-user rate limit already
    // bounds how many guesses a stolen access token buys.
    private UserAccountResult WrongCurrentPassword(UserEntity user)
    {
        logger.LogInformation("A password change by {UserName} was refused: the current password did not match.", user.UserName);
        return UserAccountResult.Invalid(CurrentPasswordIncorrect);
    }

    // MAM-4 — a conditional update claims the change: of two requests at once (a double click),
    // exactly one finds the flag the other way round and flips it, so only one of them records it.
    private async Task<bool> ChangeIsDisabledAsync(Guid userId, bool isDisabled, CancellationToken cancellationToken) =>
        await db.Users
            .Where(user => user.Id == userId && user.IsDisabled != isDisabled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsDisabled, isDisabled), cancellationToken) == 1;
}
