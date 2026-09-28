using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Setup;

/// <inheritdoc cref="ISetupService"/>
public sealed class SetupService(
    ApplicationDbContext db,
    IPasswordHasher hasher,
    IAuditTrail auditTrail,
    ISessionService sessions,
    TimeProvider time,
    ILogger<SetupService> logger) : ISetupService
{
    private const string AlreadyCompletedError = "This installation is already set up. Sign in instead.";

    public async Task<SetupStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        new(IsRequired: !await db.Installations.AnyAsync(cancellationToken));

    public async Task<SetupCompletionResult> CompleteAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        // Closed before anything else is looked at: once there is an owner, what this request carries
        // doesn't matter.
        if (await db.Installations.AnyAsync(cancellationToken))
            return SetupCompletionResult.AlreadyCompleted(AlreadyCompletedError);
        if (UserNameRules.Validate(userName) is { } userNameError)
            return SetupCompletionResult.Invalid(userNameError);
        if (PasswordRules.Validate(password) is { } passwordError)
            return SetupCompletionResult.Invalid(passwordError);

        var now = time.GetUtcNow().UtcDateTime;
        var trimmedUserName = userName.Trim();
        var owner = new UserEntity
        {
            UserName = trimmedUserName,
            NormalizedUserName = UserNameRules.Normalize(trimmedUserName),
            PasswordHash = hasher.Hash(password),
            CreatedUtc = now,
            // The owner is signed in by this very request.
            LastSignInUtc = now,
        };

        db.Users.Add(owner);
        db.Installations.Add(new InstallationEntity { OwnerUserId = owner.Id, SetupCompletedUtc = now });
        auditTrail.Record(AuditAction.SetupCompleted, AuditActor.From(owner), AuditTarget.User(owner));

        try
        {
            // One save: the owner, the row that makes them the owner and the audit row commit
            // together or not at all.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // MAM-4 — two setups can both pass the check above; the installation row's fixed key lets
            // only one insert succeed, and this one lost. Anything else (a full disk) is not a
            // conflict and stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.Installations.AnyAsync(cancellationToken))
                throw;

            logger.LogInformation(exception, "A concurrent setup created the installation owner first; this one changed nothing.");
            return SetupCompletionResult.AlreadyCompleted(AlreadyCompletedError);
        }

        logger.LogInformation("Setup completed: {UserName} is the installation owner.", owner.UserName);
        return SetupCompletionResult.Completed(await sessions.StartAsync(owner, cancellationToken));
    }
}
