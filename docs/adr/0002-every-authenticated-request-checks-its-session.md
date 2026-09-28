# 2. Every authenticated request checks its session, not only its access token

Status: accepted (0.1.0, MAM-4)

## Context

MAM-4 signs users in with a 15-minute JWT access token, which the web app keeps in memory and sends
as a bearer header, and a refresh token in the `mam_refresh` cookie, which names a row in
`UserSessions`. The usual reason to use a JWT is that the server trusts it without a lookup: a good
signature and a future expiry make it valid. That also keeps it valid until it expires, whatever
happens to its user in between.

Four things MAM-4 promises can't wait that long. **Sign-out** on a shared computer would leave a
working token in the tab's memory. **Disabling** or **deleting** a user would leave them working for
up to 15 minutes. A **password reset** is how the owner locks out someone whose password leaked —
there is no email reset and no second factor, and rotating `Jwt:Secret` ends no session, because the
refresh cookie is a random token checked against the database, not signed with that key — so a
stolen token would outlive the reset that was meant to stop it.

## Decision

**Every request that carries an access token checks the session the token names: the session must
exist for the token's user, not be revoked or expired, and the user must not be disabled. Otherwise
the request answers the same 401 `{ "error": "Sign in to continue." }` as a missing token.**

**Where.** `SessionCheckingJwtBearerEvents.TokenValidated`, after the bearer handler has checked the
signature, issuer, audience, algorithm (HS256 only) and lifetime. The token carries the user id
(`sub`) and the session id (`sid`) and grants nothing by itself; ownership is read from the
installation row too. A token naming one user with another user's live session is refused, and the
body never says which check failed.

**What ends a session.** Sign-out revokes the cookie's session, also through the token a refresh has
just replaced. Disable revokes every session of the user, and the check refuses a disabled user even
before that. A password reset revokes every session of the user except the caller's own. Delete
takes the sessions with the user (a cascade). The next request of every tab and device holding them
is refused, and so is their refresh.

**The cost.** One `EXISTS` query per authenticated request, on the session's primary key and the
user's, which SQLite answers from pages already in its cache. For a self-hosted app with a handful
of users that is noise beside the request's own queries.

| Rejected option | Why not |
| --- | --- |
| Signature and expiry only (a stateless JWT) | Sign-out, disable, delete and reset act up to 15 minutes late; a reset doesn't stop a leaked password's token |
| A shorter access token (a minute or two) | Narrows the window without closing it, and turns every minute into a refresh, which is a database write |
| An in-memory list of revoked sessions | A second store to keep in step with `UserSessions`, empty after a restart; the table already is that list |
| Rotating `Jwt:Secret` to sign everyone out | Stops everyone to stop one person, and ends no session: the refresh cookie gets a new token at once |
| A session cookie on every request instead of a bearer token | Every write would need CSRF protection; a header the web app sets from memory can't be sent by another site |

`AuthSignOutHttpTests.SignOut_stops_the_access_token_at_once_and_the_cookie_can_no_longer_refresh`,
`UsersLifecycleHttpTests.Disable_stops_the_users_access_token_and_refresh_cookie_at_once`,
`UsersLifecycleHttpTests.Delete_stops_the_users_access_token_and_refresh_cookie_at_once` and
`UsersPasswordHttpTests.ResetPassword_ends_every_session_of_the_user_at_once` assert it, each with a
token issued a moment before and valid in itself.
`AuthMeHttpTests.Me_with_the_token_of_a_disabled_user_is_401` covers a disabled user whose session
is still live, and
`AuthMeHttpTests.Me_with_a_token_signed_with_the_installation_key_but_one_flaw_is_401_with_the_sign_in_sentence`
a token that pairs one user with another user's session.

## Consequences

Sign-out, disable, delete and a reset take effect on the next request, which the web app turns into
a trip to the sign-in page: its one refresh attempt gets 401 too. Rotating `Jwt:Secret` becomes
harmless (open tabs quietly get new tokens) and useless as a lockout; the documentation says so.

Every authenticated request reads the database, so the per-request cost grows with the traffic, not
with the number of users. Nothing in Phase A comes close to where that matters.

The check is about the session, not about who holds it: a copied refresh cookie keeps working until
the session ends or its replay is noticed (a replaced token presented more than 30 seconds later
revokes the session). A request already running when its session ends is allowed to finish.

## Anti-consequence

This does not license caching the check per process or per token, which would reopen the window it
closes, nor skipping it for read-only endpoints: a disabled user must not read either. It decides
nothing about the personal access tokens of MAM-5, which are a different credential.
