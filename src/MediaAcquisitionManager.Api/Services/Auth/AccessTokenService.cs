using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediaAcquisitionManager.Api.Services.Auth;

/// <inheritdoc cref="IAccessTokenService"/>
/// <remarks>
/// The token carries ids and the user name, nothing that grants anything by itself: whether the user
/// is the owner, or still enabled, is read from the database when it matters, so a change there
/// never waits for a token to expire.
/// </remarks>
public sealed class AccessTokenService(IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    // Built on first use rather than in the constructor: a key that can't be built then fails the
    // sign-in with its explanation, instead of failing whatever resolved this service first.
    private readonly Lazy<SigningCredentials> _credentials = new(() =>
        new SigningCredentials(JwtAccessTokens.CreateSigningKey(options.Value), SecurityAlgorithms.HmacSha256));

    public AccessToken Issue(UserEntity user, Guid sessionId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expiresUtc = now + JwtAccessTokens.Lifetime;

        // Every time set explicitly: left out, the handler would stamp them from the system clock,
        // and the lifetime check reads the injected one.
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtAccessTokens.Issuer,
            Audience = JwtAccessTokens.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresUtc,
            SigningCredentials = _credentials.Value,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Sid] = sessionId.ToString(),
                [JwtRegisteredClaimNames.Name] = user.UserName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        });

        return new AccessToken(token, expiresUtc);
    }
}
