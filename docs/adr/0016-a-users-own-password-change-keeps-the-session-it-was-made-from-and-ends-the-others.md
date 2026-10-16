# 16. A user's own password change keeps the session it was made from and ends the others

Status: accepted (0.1.0, MAM-16)

## Context

Until MAM-16 only the installation owner could set a password, through Users (MAM-4): a reset ends
every session of the user except the caller's own (ADR 0002), so the owner resetting their own
password stays signed in where they did it. MAM-16 adds a Profile page where every signed-in user
changes their own password, giving the current one (`POST /api/auth/password`). The approved layout
says what happens next: "Your other devices are signed out. This one stays signed in."

The house pattern for a password change is different: it signs the user out everywhere, this tab
included, and sends them to the sign-in page with a notice. Both obvious shapes go wrong here:

- **End every session, this one too.** The person has just proved they know the current password
  and typed the new one twice; now they type it a third time to get back to the page they were on.
  It protects nothing either: whoever sent the change knew the current password, so they can sign in
  with the new one at once.
- **End no session.** A password is usually changed because someone else may know it. Whoever signed
  in with the old one (a shared computer, a lost phone, someone it leaked to) would keep a session
  that refreshes itself every 15 minutes and lasts 30 days from its last use. The change would lock
  nobody out.

Three more things constrain the endpoint. The web app reads a 401 from a protected endpoint as an
ended session and refreshes and sends the request again, so a wrong current password can't be a 401.
Checking the current password costs a PBKDF2 run, and an owner's reset that lands during that run
is how the owner takes an account back from whoever knows its old password: exactly who would be
changing it here. And sign-in's rate limit counts per address and is shared with setup.

## Decision

**A user's own password change, given the right current password, ends every other session of that
user at once and keeps the session the request came through, named by the access token's `sid`.**

**Which session stays.** The token's `sid`, as for the owner's reset of their own password
(`ISessionService.RevokeAllForUserAsync(userId, exceptSessionId)`). Other tabs of the same browser
share that session through the refresh cookie and stay signed in too. The endpoint never reads the
`mam_refresh` cookie, which the browser also sends to every `/api/auth` URL: the bearer header,
which another site can't make a browser send, is the only credential it takes.

**Who wins a race.** The current password is checked on a copy of the user, and the new hash is
made before the transaction, so PBKDF2 never holds SQLite's write lock. Then, inside one
`BEGIN IMMEDIATE` transaction: a conditional update that matches only the user as verified (same
hash, still enabled), the `UserPasswordChanged` audit row, the revocation. A reset, disable or
delete that committed during the check wins, and the change gets the wrong-password answer, as a
sign-in does (MAM-4).

**What a refusal looks like.** 400 `{ error }`, never 401: "Enter your current password." for a
blank one, the password rule's sentence for a bad new one (checked before any hash runs), and "The
current password is incorrect." for a wrong one. A current password over 128 characters is refused
without running the hash. Ten attempts a minute, then 429, counted per user
(`PasswordChangeRateLimitPolicy`): a stolen access token can't spread its guesses over many
addresses, and mistakes on Profile don't use up the sign-ins of everyone behind the same router.

| Rejected option | Why not |
| --- | --- |
| End every session, this one too, and go to sign-in | A third typing of the new password, and no protection: the sender knows it |
| End no session | A change made because the password leaked would lock nobody out |
| Take the session to keep from the refresh cookie | A second credential to trust; the token already names the session |
| Count attempts in the sign-in limit (per address) | Profile mistakes block a household's sign-ins; a token's guesses spread over addresses |
| A 401 for a wrong current password | The web app takes it for an expired token: it refreshes, resends, then shows an ended session |

`AuthPasswordHttpTests.ChangePassword_keeps_the_calling_session_and_ends_the_users_other_sessions_at_once`
asserts the decision, and `ChangePassword_leaves_the_calling_sessions_refresh_cookie_working` that
the kept session goes on refreshing.
`ChangePassword_overtaken_by_a_change_to_the_user_during_the_password_check_is_400_and_the_owners_change_stands`,
`ChangePassword_with_a_wrong_current_password_is_400_with_the_sentence_and_changes_nothing` and
`ChangePassword_is_rate_limited_per_user_whatever_the_client_address` pin the rest.

## Consequences

The user stays on Profile and sees a confirmation; every other device goes to the sign-in page with
its next request, because the session check refuses its token and its refresh (ADR 0002). Each
change writes one `UserPasswordChanged` audit row, actor and target being the user.

For the web app this endpoint and `GET /api/auth/me` are ordinary calls: a 401 from them is an
expired access token, so it refreshes once and sends them again, and skips that only for sign-in,
refresh, sign-out and setup, whose 401 means a refused password or an ended session. Profile left
open past the 15-minute token would otherwise read "Sign in to continue." `api.test.ts` pins both
lists.

A stolen access token alone can't change the password: it needs the current one, at ten guesses a
minute. The gap left on purpose: the owner's Reset password on their own row of Users still sets the
owner's password without the current one, as it did before MAM-16, so a stolen owner token can
still do that.

The session the change was made from is kept even if it is not the rightful user's. Whoever holds it
knew the current password and could have signed in with the new one anyway. When a user can't trust
their own sessions, the owner's reset of that user (which keeps none of theirs) is the lockout.

## Anti-consequence

This does not let an owner's reset of another user keep any of that user's sessions: the caller's
session is never theirs. Nor does a valid token stand in for the current password. And the refresh
cookie stays a credential for refresh and sign-out only.

## Personal access tokens (added 0.5.0, MAM-5)

MAM-5 adds personal access tokens, which a user creates on Profile for scripts and other tools
([ADR 0040](0040-a-personal-access-token-acts-as-its-user-reads-what-its-user-reads-and-writes-only-what-an-explicit-list-opens.md)).
**A user's own password change keeps every personal access token of that user; the owner's password
reset of a user revokes every one of them, the owner's reset of his own password included.**

A token is a credential the user made on purpose, for one script, and revokes one by one on the same
Profile page. A change of one's own password is routine; ending the tokens with it would stop every
script of the user at each change, while a user who thinks a token leaked revokes that token there.
A reset is the owner's lockout (ADR 0002), so it takes everything: every session but the caller's own,
and every token, his own too, since no token is the device he is working on. Profile's Password card
says "Your access tokens keep working.", and the owner's Reset password dialog that the user's access
tokens stop working.

The gap left on purpose: whoever holds one of the user's sessions can create a token on Profile, and
that token outlives the user's own password change. The user sees it in the Profile's list and
revokes it, and the owner's reset or disable of the user revokes it too — a create racing that reset
or disable included, which stores nothing (ADR 0002's MAM-5 section). A token can't create a token,
nor change the password: both refuse every token (ADR 0040).
`UsersPasswordHttpTests.ResetPassword_revokes_every_personal_access_token_of_the_user_with_a_revoked_audit_row_each`
and `ResetPassword_of_the_owners_own_password_revokes_the_owners_tokens_and_keeps_the_calling_session`
assert that a reset revokes the user's tokens, the owner's reset of his own password included;
`AuthPasswordHttpTests.ChangePassword_keeps_the_users_personal_access_tokens_working` that a user's
own change keeps them; and
`PersonalAccessTokensAccessHttpTests.ChangePassword_with_a_token_is_403_runs_no_hash_and_changes_nothing`
that a token can't change the password.
