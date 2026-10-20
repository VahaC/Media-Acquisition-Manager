using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MediaAcquisitionManager.Api.Extensions;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — lets a request in on a personal access token (<c>Authorization: Bearer mam_pat_…</c>) as the token's user,
/// with the token's access in its claims; the access itself is checked by
/// <see cref="PersonalAccessTokenAccessHandler"/> in the default policy (ADR 0040).
/// </summary>
/// <remarks>
/// <para>
/// The ADR 0002 check, for a token: on every request, never cached, the token must exist by its hash, not be revoked or
/// expired, and its user must exist and not be disabled — so revoking a token, disabling or deleting its user takes
/// effect on the very next request. Every failure gets the one 401 a session's failure gets
/// (<see cref="SignInRequiredResponse"/>), whichever check it was.
/// </para>
/// <para>
/// Every failed attempt takes one of the address's sign-in attempts (<see cref="SignInAttemptLimiter"/>), and while
/// those are used up a token from that address is answered 429 without being looked up: otherwise a run guessing tokens
/// would go on at full speed, its guesses merely refused. A request let in costs nothing. The lookups still under way
/// count as attempts that may yet be taken, so a burst of guesses sent at once is held to the same ten as guesses sent
/// one after the other: the attempt is counted only once the lookup has failed, and checking the bucket alone would let
/// every guess already past the check be looked up.
/// </para>
/// <para>
/// "Last used" is written only when the lookup shows it is due. Even an update that matches no row takes SQLite's write
/// lock, so writing on every request would make each token request wait behind every write in the app, where a
/// session's request reads straight past it (WAL).
/// </para>
/// <para>
/// Built per request from the request's services (handlers always are), so the DbContext is the request's own.
/// </para>
/// </remarks>
public sealed class PersonalAccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApplicationDbContext db,
    TimeProvider time,
    SignInAttemptLimiter attempts) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    /// <summary>"Last used" is kept to the minute: a script calling every second would otherwise write the row on every
    /// request, and SQLite has one writer for the whole app. Checked against the row the lookup read before any update
    /// is sent, since even an update that changes nothing waits for that writer.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    // Set when the address's attempts are used up, so the challenge answers 429 instead of 401.
    private static readonly object TooManyAttemptsKey = new();

    // One message for every failure; the log shouldn't say more about a guess than the response does.
    private const string Refused = "The personal access token was refused.";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PersonalAccessTokenBearer.TryRead(Request, out var presented))
            return AuthenticateResult.NoResult();

        var address = SignInRateLimitPolicy.PartitionKeyOf(Context);
        // MAM-5 — started before the lookup and ended after it, so the lookups under way share the address's attempts
        // with the failures already counted (SignInAttemptLimiter).
        if (!attempts.TryStartLookup(address))
        {
            Context.Items[TooManyAttemptsKey] = true;
            return AuthenticateResult.Fail(Refused);
        }

        var failed = false;
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            var token = PersonalAccessTokenSecret.IsWellFormed(presented)
                ? await FindActiveAsync(PersonalAccessTokenSecret.HashOf(presented), now)
                : null;
            if (token is null)
            {
                failed = true;
                return AuthenticateResult.Fail(Refused);
            }

            // Asked of the row just read, so a token used within the minute never reaches for the write lock; the
            // update's own condition still settles two requests at once.
            if (token.LastUsedUtc is not { } lastUsedUtc || lastUsedUtc <= now - LastUsedResolution)
                await MarkUsedAsync(token.Id, now);

            // The claim names a session's JWT uses (sub, name; MapInboundClaims = false), so everything that reads the
            // user reads a token's the same way. No sid: whatever needs a session is refused to tokens.
            var identity = new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, token.UserId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Name, token.UserName),
                    new Claim(PersonalAccessTokenClaims.TokenId, token.Id.ToString()),
                    new Claim(PersonalAccessTokenClaims.Access, token.Access.ToString()),
                ],
                Scheme.Name,
                JwtRegisteredClaimNames.Name,
                roleType: null);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        finally
        {
            // A refused token takes its attempt in the same step that ends its lookup. A lookup that threw (the client
            // gone, the database busy) refused nobody, so it costs nothing either.
            attempts.EndLookup(address, failed);
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!Context.Items.ContainsKey(TooManyAttemptsKey))
        {
            await SignInRequiredResponse.WriteAsync(Context);
            return;
        }

        // The rate limiter's own answer (RateLimitingServiceCollectionExtensions.OnRejected): a fixed window's
        // Retry-After is one whole window, never earlier than the next attempt can succeed.
        Response.Headers.RetryAfter = ((int)Math.Ceiling(SignInRateLimitPolicy.Window.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await ApiErrorBody.WriteAsync(Context, StatusCodes.Status429TooManyRequests, RateLimitingServiceCollectionExtensions.TooManyAttempts);
    }

    // One query: the token by its hash, active, of a user who exists and isn't disabled — the join drops a disabled
    // user's token as the session check's Users.Any does, and brings the name the claims carry.
    private Task<ActiveToken?> FindActiveAsync(string hash, DateTime now) =>
        db.PersonalAccessTokens
            .Where(PersonalAccessTokenRules.IsActive(now))
            .Where(token => token.TokenHash == hash)
            .Join(
                db.Users.Where(user => !user.IsDisabled),
                token => token.UserId,
                user => user.Id,
                (token, user) => new ActiveToken(token.Id, token.UserId, user.UserName, token.Access, token.LastUsedUtc))
            .SingleOrDefaultAsync(Context.RequestAborted);

    // MAM-5 — conditional, so of two requests at once that both read it due, one writes; the caller asks first whether it
    // is due at all, which spares the write lock on every other request of the minute.
    private Task MarkUsedAsync(Guid tokenId, DateTime now)
    {
        var usedBefore = now - LastUsedResolution;
        return db.PersonalAccessTokens
            .Where(token => token.Id == tokenId && (token.LastUsedUtc == null || token.LastUsedUtc <= usedBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.LastUsedUtc, (DateTime?)now), Context.RequestAborted);
    }

    private sealed record ActiveToken(Guid Id, Guid UserId, string UserName, PersonalAccessTokenAccess Access, DateTime? LastUsedUtc);
}
