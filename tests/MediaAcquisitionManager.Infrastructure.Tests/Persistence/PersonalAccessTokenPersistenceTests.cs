using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-5 — personal access tokens pinned on the migrated schema (ADR 0040): a user's tokens go with the user, and nobody
// else's; a hash names exactly one token, as every token request looks one up by it; a token belongs to an account that
// exists; the access is stored by name, and every instant to the tick, as the "once a minute" checks compare them.
public sealed class PersonalAccessTokenPersistenceTests : DatabaseTestBase
{
    public PersonalAccessTokenPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Deleting_a_user_deletes_their_tokens_and_no_one_elses()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var member = await CreateUserAsync(db, "member");
        var owner = await CreateUserAsync(db, "owner");
        await Tokens(db).Create(member.Id, PersonalAccessTokenAccess.ReadAndDownload, "Laptop", expiresUtc: null);
        await Tokens(db).Create(member.Id, PersonalAccessTokenAccess.ReadOnly, "Old script", expiresUtc: null, revokedUtc: DateTime.UtcNow);
        var ownersOwn = await Tokens(db).Create(owner.Id, PersonalAccessTokenAccess.ReadOnly, "Owner's script", expiresUtc: null);

        //Act
        await db.Users.Where(user => user.Id == member.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a token is its user's, and means nothing without them.
        await using var check = CreateContext();
        Assert.Equal([ownersOwn.Token.Id], await check.PersonalAccessTokens.AsNoTracking().Select(token => token.Id).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_token_with_a_hash_already_stored_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db, "owner");
        var first = await Tokens(db).Create(owner.Id, PersonalAccessTokenAccess.ReadOnly, "First", expiresUtc: null);
        db.PersonalAccessTokens.Add(new PersonalAccessTokenEntity
        {
            UserId = owner.Id,
            Name = "Second",
            Prefix = first.Token.Prefix,
            TokenHash = first.Token.TokenHash,
            Access = PersonalAccessTokenAccess.ReadAndDownload,
            CreatedUtc = DateTime.UtcNow,
        });

        //Act
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Contains("UNIQUE constraint failed: PersonalAccessTokens.TokenHash", sqlite.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_of_an_account_that_does_not_exist_is_refused()
    {
        //Arrange
        await using var db = CreateContext();

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Tokens(db).Create(Guid.NewGuid(), PersonalAccessTokenAccess.ReadOnly, "Nobody's", expiresUtc: null));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Contains("FOREIGN KEY constraint failed", sqlite.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_access_is_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db, "owner");
        await Tokens(db).Create(owner.Id, PersonalAccessTokenAccess.ReadAndDownload, "Claude", expiresUtc: null);

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT \"Access\" AS \"Value\" FROM \"PersonalAccessTokens\"")
            .SingleAsync(cancellationToken);

        //Assert
        // By name, as every enum here is: reordering the enum never changes what a stored token may do.
        Assert.Equal("ReadAndDownload", stored);
    }

    [Fact]
    public async Task Every_instant_of_a_token_is_kept_to_the_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db, "owner");
        var createdUtc = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var lastUsedUtc = createdUtc.AddMinutes(1).AddTicks(1);
        var expiresUtc = createdUtc.AddDays(90);
        var revokedUtc = createdUtc.AddHours(1).AddTicks(7);
        var planted = await Tokens(db).Create(
            owner.Id, PersonalAccessTokenAccess.ReadOnly, "Script", expiresUtc, revokedUtc: revokedUtc, lastUsedUtc: lastUsedUtc, createdUtc: createdUtc);

        //Act
        await using var read = CreateContext();
        var row = await read.PersonalAccessTokens.AsNoTracking().SingleAsync(token => token.Id == planted.Token.Id, cancellationToken);

        //Assert
        Assert.Equal(
            (createdUtc.Ticks, (long?)lastUsedUtc.Ticks, (long?)expiresUtc.Ticks, (long?)revokedUtc.Ticks),
            (row.CreatedUtc.Ticks, row.LastUsedUtc?.Ticks, row.ExpiresUtc?.Ticks, row.RevokedUtc?.Ticks));
    }

    private static PersonalAccessTokenDataFactory Tokens(ApplicationDbContext db) => new(db, TimeProvider.System);

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");
}
