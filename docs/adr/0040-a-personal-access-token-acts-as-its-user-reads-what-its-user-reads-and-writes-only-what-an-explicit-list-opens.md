# 40. A personal access token acts as its user, reads what its user reads, and writes only what an explicit list opens

Status: accepted (0.5.0, MAM-5)

## Context

Until MAM-5 the only credential the API took was the 15-minute access token of a sign-in, tied to a
session and renewed through the `mam_refresh` cookie, which only a browser on `/api/auth` keeps
(ADR 0002). A script, a cron job or, with MAM-45, an AI assistant has no browser. Phase E (Vasyl's
request of 2026-10-14) gives each user personal access tokens for them: a long-lived bearer value the
user creates on Profile, names, and revokes.

A long-lived credential has to answer what it may do, and the obvious answers go wrong here:

- **The user's whole power.** A token sits in a script file or an assistant's configuration and, on
  a LAN, crosses the network in clear (the app speaks plain HTTP). Leaked, an owner's token could
  delete a quarantined copy permanently, change an integration or reset a password, where Phase C
  rests on the owner pressing those buttons himself.
- **Allow by default, with a deny list.** Every endpoint added later — MAM-45's, or any after it —
  would be open to tokens until somebody remembered to close it.
- **Read is "safe methods only", the house rule's wording.** Both searches are POSTs
  (`POST /api/searches`, `POST /api/releases/search`), and a send takes only a release of one of the
  caller's own searches of the last day (`CallerSearches`), so a read-only token couldn't search and
  a token allowed to send couldn't either.
- **A token principal through today's code.** `GET /api/auth/me`, `POST /api/auth/password` and the
  Users writes read the session id (`sid`) and throw without one, a 500. `DownloadService.CancelAsync`
  also ends the replacement a job carries, and every endpoint under `Controllers/Settings` that
  reads answers with personal data (Users), addresses and user names (Integrations) or a live call
  to a download client (Destinations).

The ticket's rule, approved by Vasyl on 2026-10-14: `Read only` reads everything a user reads;
`Read and download` may also send a release (Keep both only), cancel following and mark Not
interested; no token ever schedules a replacement, removes a copy into the quarantine, restores,
deletes permanently, writes a setting, or touches the account's password, sessions or tokens; and a
write is open to a token only when it is on an explicit list.

## Decision

**A personal access token acts as its user — that user's id and name, never more than the user
may — and reads what that user reads. A write is open to a token only when its endpoint carries an
explicit mark; every other write, every endpoint under `Controllers/Settings`, the account's
session and password, and the token management itself refuse any token with 403 and a sentence.
`Read only` opens the reads and the two searches; `Read and download` adds exactly three writes: a
send (Keep both), cancelling the following of a job, and Not interested.**

**The credential.** `mam_pat_` followed by 32 random bytes in base64url, 43 characters, 51 in all.
The database keeps its SHA-256 hash in lowercase hex (`PersonalAccessTokens.TokenHash`, a unique
index) and its first 14 characters (`Prefix`, shown as `mam_pat_AbC123…`); the whole value leaves the
server once, in the create answer. A plain SHA-256 suffices, as for the refresh cookie: 256 random
bits can't be guessed from the hash, and a slow hash would cost a PBKDF2 run on every request. Each
token has a name (at most 100 characters), an access level (`ReadOnly`, `ReadAndDownload`) and a
lifetime of 30 days, 90 days, a year or none (`ExpiresUtc` null).

**Who it is.** A policy scheme is the default: a request whose `Authorization` header reads
`Bearer mam_pat_…` (the word `Bearer` in any letter case) goes to
`PersonalAccessTokenAuthenticationHandler`, every other one to JwtBearer, whose scheme keeps the
name `Bearer`. No request is let in on a token without the handler looking its hash up — not
revoked, not expired, its user not disabled, ADR 0002's check, never cached — and the request let in
gets the JWT's own `sub` and `name` claims plus the token's id and access level, never a `sid`.
Ownership is read from the installation row as for a sign-in: a member's token reads that member's
jobs and searches only, an owner-only read answers a member's token "Only the installation owner can
do this.", and an owner's token reads what the owner reads outside Settings, the replacements and the
TMDb link lists included.

**The marks.** The default authorization policy is a signed-in user plus
`PersonalAccessTokenAccessRequirement`, so every plain `[Authorize]` enforces it; there is still no
fallback policy, and the anonymous endpoints stay anonymous. For a token the requirement reads the
endpoint's metadata: `[DenyPersonalAccessToken]` refuses; a GET or HEAD passes;
`[PersonalAccessTokenRead]` passes — `SearchesController.Create` and `ReleaseSearchController.Search`;
`[PersonalAccessTokenWrite]` passes for `Read and download` only — `DownloadsController.Create`,
`DownloadsController.Cancel` and `RecommendationsController.Dismiss`; anything else refuses, an
endpoint with no method constraint included. The marks are endpoint metadata, not MVC filters, so
MAM-45's `/mcp` endpoint, a minimal API, takes the same rule.

**Never for a token.** `[DenyPersonalAccessToken]` sits on `AuthController.Me` and
`AuthController.ChangePassword` — the actions, as sign-in, refresh and sign-out are anonymous — and
on `PersonalAccessTokensController` (`/api/profile/tokens`). `SettingsWritesAreOwnerOnlyConvention`
adds it to the endpoint metadata of every action under `Controllers/Settings`, reads included:
Users, Integrations, Media folders, the settings Destinations and the owner's revoke-all. Nothing a
script needs lives there: the sources' status is `GET /api/library/sources` and a release's
destinations are `GET /api/downloads/options`. The authorization middleware runs before the owner
filter, so a token there reads the token's sentence, never the owner's.

**Keep both, by construction.** `SendDownloadRequest` has no replacement field, so a token's send
can't schedule a replacement, whatever its body holds. A token's cancel that takes a job carrying a
replacement still being followed is refused with nothing changed
(`DownloadCancelStatus.ReplacementNeedsTheWebApp`), as the cancel would end that replacement too;
the owner cancels it on the Jobs page. `DownloadService.CancelAsync` asks only once its conditional
cancel has taken the job, inside the transaction that holds SQLite's write lock, and leaves without
committing, so the job's cancel rolls back; a cancel that takes nothing — the job already ended, or
still being sent — answers a token the 409 a session gets, whatever replacement the job carries.
Undoing Not interested stays closed: it isn't on the list.

**One 401.** A wrong, unknown, malformed, revoked or expired token, and a disabled or deleted user's,
answer exactly what a missing or ended sign-in does — 401, `WWW-Authenticate: Bearer`,
`{ "error": "Sign in to continue." }` — written by one shared writer (`SignInRequiredResponse`), so
the bytes are the same by construction and never say which check failed.

**Guessing shares the sign-in limit.** A failed token attempt takes one permit of its address's
bucket in `SignInAttemptLimiter`, the store `SignInRateLimitPolicy` now draws from (10 a minute per
address, every sign-in and setup request taking one, whatever its outcome). While that bucket is
empty, every token request from the address to an endpoint that needs a signed-in user answers 429
`{ "error": "Too many attempts. Wait a minute and try again." }` with `Retry-After`, without the
token being looked up, so guessing can't go on. The lookups under way count too: the handler starts
one only while the bucket has more permits left than lookups from that address are running
(`SignInAttemptLimiter.TryStartLookup`), and a failed one takes its permit as it ends
(`EndLookup`), so a burst of guesses sent at once gets no more lookups than guesses sent one after
the other. A request with a good token takes nothing, and a failed sign-in token (JWT) still counts
nothing.

**What a refusal says.** 403 `{ error }` with one of three sentences, each naming where to go:

| Refused | Sentence |
| --- | --- |
| A send, cancel or Not interested with a `Read only` token | This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README. |
| Anything a token may never do | An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README. |
| A cancel that would end a replacement | This job also replaces a copy in the library, and an access token never touches a replacement. Cancel it on the Jobs page of the web app; see “Scripts and the API” in the README. |

**What ends a token.** Its user's **Revoke** on Profile; the owner's **Revoke tokens** on Users
(`DELETE /api/settings/users/{id}/tokens`); the owner's password reset of the user, his own
included; disabling the user, so Enable brings none back — the Users page asks before a **Disable**
that revokes tokens, saying how many, and disables a user without any at once, as before; deleting
the user, whose rows go with it. A user's own password change ends none (ADR 0016's MAM-5 section).
Rotating `Jwt:Secret` ends none: tokens aren't signed with it. And none outlives the revocation it
raced: `PersonalAccessTokenService.CreateAsync` checks the creating session again inside its write
transaction, with the session check's own condition, and a session ended meanwhile — a reset,
disable or delete committed since the request was let in — stores nothing
(`PersonalAccessTokenCreateStatus.SessionEnded`) and answers the 401 that session's next request
gets.

**What the audit trail keeps.** `PersonalAccessTokenCreated` (actor the user),
`PersonalAccessTokenRevoked` (actor whoever revoked, one row per token — the owner's for
revoke-all, a reset or a disable) and `PersonalAccessTokenRefused` (actor the token's user, target
the token), at most one refused row per token a minute through a conditional update of
`LastRefusedUtc`, while every refusal logs a warning with its method and path. "Last used" is written
at most once a minute the same way (`LastUsedUtc`). Both read their column first and send no update
within the minute: in SQLite even an update that matches no row waits for the one write lock, so a
token request would otherwise queue behind every write in the app, where a session's request reads
straight past it.

| Rejected option | Why not |
| --- | --- |
| A token with its user's whole power | A leaked owner's token could delete a quarantined copy permanently, change an integration or reset a password |
| Allow by default, with a deny list | Every endpoint added later is open to tokens until someone remembers to close it |
| Read = GET and HEAD only | Both searches are POSTs, and a send needs one of the caller's own searches, so neither level could work |
| Scopes per endpoint, ticked by the user | A matrix nobody reads; two levels say what a script may do |
| An owner's token reads Settings | Users is personal data, Integrations names addresses and user names, Destinations ask clients live; no script needs them |
| `GET /api/auth/me` for a token | It describes the account's session (`sessionExpiresUtc`) and fails without `sid`; a "who am I" for tools belongs to MAM-45, if anywhere |
| A 401 sentence of its own for tokens | It would tell a guesser that the value has a token's shape, and a client would need a second case |
| A limit for tokens apart from sign-in | Two buckets per address double the guesses; one limit is one rule |
| A refused audit row per request | A script looping on a refused write floods `AuditEvents`; the warning log keeps every one |
| A salted slow hash | A lookup by hash is needed on every request, and 256 random bits leave nothing to slow down |
| A token's cancel ends the job's replacement too | A token would end a replacement through the back door while `POST /api/replacements/{id}/cancel` refuses it |
| Refuse the token's cancel by looking at the replacement before cancelling | A job that has already ended, its replacement waiting in review, would answer a token 403 "Cancel it on the Jobs page" for a cancel that changes nothing, where a session gets 409; and an owner's Retry could bring an ended replacement back between the look and the cancel |

Each decision is pinned. The one 401, byte for byte against a refused session token's, for every
failure: `PersonalAccessTokenAuthenticationHttpTests.Get_with_a_refused_token_is_the_same_401_a_refused_session_gets`.
Acting as its user: `Get_with_a_members_token_reads_only_that_members_jobs`. The shared limit and
its 429: `Failed_tokens_use_up_the_sign_in_attempts_of_their_address_then_every_token_from_it_is_429`,
`Failed_tokens_and_failed_sign_ins_from_one_address_share_its_ten_attempts`,
`Failed_tokens_sent_at_once_get_no_more_lookups_than_their_address_has_sign_in_attempts` and
`Gets_with_a_valid_token_use_none_of_the_sign_in_attempts_of_their_address`, the anonymous endpoints
answering as without a token by `Health_with_a_token_valid_or_not_answers_as_without_one`. "Last
used": `Gets_with_a_token_write_last_used_on_the_first_use_then_at_most_once_a_minute` and
`Get_with_a_token_used_within_the_minute_answers_while_another_connection_holds_the_write_lock`.

Deny by default and the explicit list, in `PersonalAccessTokenWritesHttpTests`:
`Discovery_finds_exactly_the_three_writes_open_to_read_and_download`,
`Discovery_finds_exactly_the_two_posts_that_only_read`,
`Discovery_finds_the_refusal_on_the_account_the_token_management_and_nothing_else_outside_settings`
and `Discovery_finds_the_refusal_on_every_settings_action_reads_included` keep the lists fixed, so the
test guards itself; `Every_other_write_with_an_owners_token_is_403_with_the_sentence_and_changes_nothing`,
with a token of each access, and `Every_refused_read_with_an_owners_token_is_403_with_the_sentence`
send them as the owner; `The_three_writes_with_the_owners_read_only_token_are_403_with_the_read_only_sentence`;
and one refused row a minute by `Refusals_of_one_token_make_one_audit_row_a_minute_and_a_warning_each`
and `Refusal_within_the_minute_answers_while_another_connection_holds_the_write_lock`. An endpoint
added later: `PersonalAccessTokenConventionHttpTests.Unmarked_write_with_a_token_is_403_with_the_sentence`.
Never for a token: `PersonalAccessTokensAccessHttpTests.Endpoint_with_a_token_is_403_with_the_sentence_and_creates_or_revokes_nothing`,
`Me_with_a_token_is_403_with_the_sentence_not_a_500` and
`ChangePassword_with_a_token_is_403_runs_no_hash_and_changes_nothing`.

Keep both: `DownloadsPersonalAccessTokenHttpTests.Create_with_a_token_is_keep_both_whatever_replacement_fields_the_body_carries`;
the refused cancel, `Cancel_with_a_token_of_a_job_whose_replacement_is_followed_is_403_and_changes_nothing`;
the cancel that takes nothing, `Cancel_with_a_token_of_a_job_no_cancel_can_take_is_the_409_a_session_gets_whatever_its_replacement`;
and the undo, `RecommendationDismissalsPersonalAccessTokenHttpTests.Undismiss_with_a_token_is_403_and_keeps_the_mark`.

What ends a token: `PersonalAccessTokensHttpTests.Revoke_is_204_and_the_token_stops_working_with_its_next_request`,
`UsersTokensHttpTests.RevokeTokens_is_204_and_stops_every_token_of_the_user_at_once`,
`UsersPasswordHttpTests.ResetPassword_revokes_every_personal_access_token_of_the_user_with_a_revoked_audit_row_each`
and `ResetPassword_of_the_owners_own_password_revokes_the_owners_tokens_and_keeps_the_calling_session`,
`UsersLifecycleHttpTests.Disable_revokes_the_users_personal_access_tokens_at_once_with_a_revoked_audit_row_each`,
`Enable_does_not_bring_back_the_personal_access_tokens_the_disable_revoked` and
`Delete_removes_the_users_personal_access_tokens_with_them_and_stops_them_at_once`. A create that
raced one: `PersonalAccessTokensHttpTests.Create_racing_the_owners_reset_disable_or_delete_of_the_user_is_401_and_stores_no_token`
and `PersonalAccessTokenCreateSessionTests.CreateAsync_from_a_session_that_has_ended_is_session_ended_and_stores_nothing`.
The user's own change keeping them: `AuthPasswordHttpTests.ChangePassword_keeps_the_users_personal_access_tokens_working`.
The secret given once and only its hash and prefix kept:
`PersonalAccessTokensHttpTests.Create_is_201_with_the_token_and_its_secret_and_stores_only_its_hash_and_prefix`;
the one new table: `AddPersonalAccessTokensMigrationTests.Upgrade_creates_the_tokens_table_and_its_two_indexes_and_touches_no_other_table`.
In the web app, `UsersPage.test.tsx`'s "asks before disabling a user with an active access token,
and disables them only once it is accepted" and "disables a user without an active access token at
once, without asking, and shows the refreshed row" pin Disable's confirmation.

## Consequences

A script gets a credential of its own that works until its user, or the owner, ends it, at the cost
of one lookup per request, as for a sign-in. Every write a token should reach later needs its mark
and a line in the convention test's list; until then it answers the token's 403. MAM-45's `/mcp`
endpoint is a POST, so it stays closed to tokens until it is marked on purpose.

The token crosses a plain-HTTP network as it is: whoever reads the traffic can use it until it is
revoked or expires. The README says so, with the lifetimes and **Revoke** as the remedy. A token
needs no CSRF protection, which specification §4's **Security** asks for state-changing actions: a
browser never attaches a bearer header by itself.

Behind a reverse proxy, or Docker's gateway, every client shares one address, so a script with a
revoked token, run in a loop, uses up the household's sign-ins for as long as it runs; the README
says to fix or stop the script. A wrong token counts wherever it is sent, the health endpoints
included, since every request is authenticated first; but an endpoint that needs no signed-in user
answers as it does without a token, never 401 or 429, as only a challenge writes those.

The audit trail shows at most one refusal a minute per token, and no method or path, which have no
column there; the log has every refusal with both.

## Anti-consequence

This doesn't license opening a write by matching a path, a write mark on any action under
`Controllers/Settings` or `Controllers/Replacements`, or a token acting for anyone but its user. A
token is no second sign-in: it has no session, no refresh and no cookie. And it relaxes nothing of
ADR 0002: the check runs on every request, reads included, and is never cached.

## The MCP endpoint (added 0.5.0, MAM-45)

The Consequences above left `/mcp` closed to tokens until it was marked on purpose. MAM-45 marks it
with a fourth mark, not with either of the two above: one POST there carries any tool call, so
`[PersonalAccessTokenRead]` would let a `Read only` token's send through and a session with it, and
`[PersonalAccessTokenWrite]` would shut a `Read only` token out of its reads. **`/mcp` carries
`[PersonalAccessTokenOnly]`: any token gets in, once `[DenyPersonalAccessToken]` is checked, and a
signed-in session gets 403 — the one place a session is refused — with "The MCP endpoint takes a
personal access token, not a web app sign-in. Create a token on your Profile page and connect with
it; see “Connect an AI assistant (MCP)” in the README." (`SessionNotAccepted`), with no audit row, as
a session has no token row. The endpoint's own filter then applies this ADR's two levels tool by
tool:** a tool is a read when it says so (`ReadOnly = true`), a write only when its method carries
`[PersonalAccessTokenWrite]` — the same mark, the same three writes: `send_download`, `cancel_job`,
`dismiss_recommendation` —, and a tool that is neither is no token's, deny by default as above. A
refused call answers this ADR's sentence as the tool's error and is recorded the same way, its
method `tools/call` and its path the tool's name; `cancel_job` refuses a job whose replacement is
still followed, as `DownloadsController.Cancel` does. The "who am I" the rejected `GET /api/auth/me`
left to MAM-45 is its `get_status`, built from what a member reads and no Settings.
`PersonalAccessTokenRefusalResultHandler` now answers every refusal of the handler and records only
a token's; one token's requests to `/mcp` are held to 60 a minute.

How the endpoint and its tools are built, and why, is
[ADR 0041](0041-an-ai-assistant-reaches-the-app-through-one-mcp-endpoint-for-personal-access-tokens-alone-where-each-tool-is-a-read-or-one-of-the-three-writes-judged-per-call.md).
`PersonalAccessTokenConventionHttpTests.Post_marked_for_tokens_alone_with_a_token_of_either_access_is_2xx`
and `Post_marked_for_tokens_alone_with_a_session_is_403_with_the_sentence_and_records_nothing` pin the
mark on a probe endpoint, and
`McpToolListHttpTests.Discovery_finds_exactly_the_twelve_tools_each_a_read_or_one_of_the_three_writes_and_never_both`
keeps the tools' list fixed, as `PersonalAccessTokenWritesHttpTests` keeps the HTTP API's.
