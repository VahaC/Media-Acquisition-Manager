using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IUserDataFactory"/>
public sealed class UserDataFactory(ApplicationDbContext db, IPasswordHasher hasher, TimeProvider time) : IUserDataFactory
{
    public async Task<UserEntity> Create(string userName, string password, bool isDisabled = false)
    {
        var user = new UserEntity
        {
            UserName = userName,
            NormalizedUserName = UserNameRules.Normalize(userName),
            PasswordHash = hasher.Hash(password),
            IsDisabled = isDisabled,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return user;
    }

    public Task SetLanguage(Guid userId, string? language) =>
        db.Users.Where(user => user.Id == userId).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Language, language));
}
