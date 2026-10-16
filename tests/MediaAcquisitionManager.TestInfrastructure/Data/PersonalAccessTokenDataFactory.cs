using System.Buffers.Text;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IPersonalAccessTokenDataFactory"/>
public sealed class PersonalAccessTokenDataFactory(ApplicationDbContext db, TimeProvider time) : IPersonalAccessTokenDataFactory
{
    public async Task<IssuedPersonalAccessToken> Create(
        Guid userId,
        PersonalAccessTokenAccess access,
        string name,
        DateTime? expiresUtc,
        DateTime? revokedUtc = null,
        DateTime? lastUsedUtc = null,
        DateTime? createdUtc = null)
    {
        // The format spelled out, as the hash is (PersonalAccessTokenHash): mam_pat_ and 32 random bytes in base64url.
        var secret = "mam_pat_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var token = new PersonalAccessTokenEntity
        {
            UserId = userId,
            Name = name,
            Prefix = secret[..14],
            TokenHash = PersonalAccessTokenHash.Of(secret),
            Access = access,
            ExpiresUtc = expiresUtc,
            RevokedUtc = revokedUtc,
            LastUsedUtc = lastUsedUtc,
            CreatedUtc = createdUtc ?? time.GetUtcNow().UtcDateTime,
        };

        db.PersonalAccessTokens.Add(token);
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return new IssuedPersonalAccessToken(token, secret);
    }
}
