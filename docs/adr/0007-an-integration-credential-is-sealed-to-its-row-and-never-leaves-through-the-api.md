# 7. An integration credential is sealed to its row and never leaves through the API

Status: accepted (0.1.0, MAM-7)

## Context

MAM-7 stores the credentials of the owner's services: Jellyfin's and the Torznab feed's API keys and
qBittorrent's password. Each one controls a service on the owner's network — Prowlarr's key reaches
every indexer account it holds (ADR 0004), qBittorrent's password adds and deletes torrents. The
house rule is that secrets are encrypted at rest and never returned, and MAM-1 already provisions
`Encryption:Key` for them in `.secrets/`.

The ways out are many. A copy of `mam.db` travels in backups. Logs ship off the box, and the HTTP
client factory's logging handlers write every request URI at Information; .NET 9 and later mask its
query as `*`, but one switch (`DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION`) turns that off, and the
Torznab key rides in the query. MVC logs a controller action's arguments at Trace through their
`ToString`. A service can echo a key back in its error text. A redirect carries a request,
credential and all, to wherever it points. And an API that returns a secret, even to the owner,
hands it to whoever holds the owner's session.

## Decision

**An integration's password and API key are stored only as AES-256-GCM ciphertext sealed to their
integration and field, are never returned by the API or written to a log, and leave the server only
in requests to the address the owner saved or typed, which never follow a redirect.**

**The format.** `v1:` + base64 of a fresh 12-byte random nonce, the ciphertext and the 16-byte tag,
with `integration-connection/{id:N}/password` or `…/api-key` as associated data: authenticated, not
stored. A value copied to another row or into the other column fails to open instead of being sent
to that row's server. That stops a mistake, not an attacker: whoever can write the database can
change a row's address instead. The prefix leaves room for a later format, such as one with a key id.

**The key is read on first use**, like `Jwt:Secret` in `AccessTokenService`: `dotnet ef` and the
image build need none, and a value that isn't base64 of exactly 32 bytes fails the first save or test
that needs it with a trace id and a log line naming `MAM_Encryption__Key` — never its value — rather
than stopping the whole app, sign-in included.

**Nothing decrypts what it doesn't send.** Reads, and a save that keeps a secret, leave the column
byte for byte, so a value sealed under a lost key opens again once the key is restored. A stored
secret that can't be opened fails only its own integration's test, with 409 and a sentence asking to
enter it again; the adapter is never called, because sending nothing where a key belongs would read
as the service refusing it. It is logged as a warning, without the value, and never cleared.

**The API speaks presence, not values.** An integration answers `hasPassword` and `hasApiKey`; a
change is `{ mode: "Set", value }`, `{ mode: "Clear" }`, or the field left out to keep it. The
records that carry a secret on the server (`SecretValueUpsert`, and so the create, update and test
requests that hold one; `IntegrationConnectionSettings`; a release's payload locator) print it
masked.

**Masked twice, logged never.** `HttpIntegrationAdapter` finishes every test message by masking the
call's secrets and any session the adapter opened (Template Method), and the service masks the
message again after the adapter returns, which also covers an adapter that isn't HTTP. Text a
service sent is masked before it is cleaned and cut to length as well (`RemoteText`), since a key
cut in half no longer matches: once as sent and once after the cleaning, which can join a key a
zero-width character split. An exception's text never becomes a message. The integration HTTP
clients have no logging handlers, follow no redirect (a 3xx is reported with its target, without
query or user info), keep no cookie container (the qBittorrent adapter sends the one session cookie
it means to) and retry nothing, since a failed qBittorrent sign-in counts towards its ban. An
address can't hold user info, a query or a fragment, so no credential sits in a column that is
stored in clear and returned by every read.

**No private-address guard.** These services live on the owner's LAN by design, and only the owner
can enter an address. The server-side request forgery that specification §4 warns about comes from
addresses an indexer supplies; the guard belongs to MAM-13's fetch of release links.

| Rejected option | Why not |
| --- | --- |
| No associated data | A ciphertext copied to another row opens there and goes to that row's server |
| The service's origin in the associated data too | A host rename or a move to HTTPS makes every key unreadable; offered as an option, not built |
| Refusing to start on a bad key (`ValidateOnStart`) | Stops sign-in too, and departs from the `Jwt:Secret` precedent |
| Returning a masked secret (`••••1234`) | Leaks part of it, and a form would send the mask back as a value |
| Clearing a secret that can't be opened | Destroys a value that opens again once the key comes back from a backup |
| Following redirects to the same host | Still sends the key somewhere the owner didn't enter; the fix is the address |

`IntegrationsConnectionTestHttpTests.Test_with_a_secret_copied_from_another_integration_is_409_and_never_calls_the_adapter`,
`IntegrationsSecretsHttpTests.Every_settings_read_answers_without_a_planted_secret_or_its_ciphertext`
and `IntegrationsSecretsHttpTests.Nothing_logged_while_integrations_are_used_carries_a_secret`
assert it, `SecretValueUpsertTests.ToString_of_every_request_that_carries_a_secret_prints_neither_value`
the requests MVC logs, and
`TorznabAdapterTests.TestConnection_masks_a_key_the_error_description_echoes_across_its_length_cap`
the masking before the cut. `IntegrationSecretPurposeTests.For_is_stable` pins the associated data,
`IntegrationsUpdateHttpTests.Update_that_leaves_a_secret_out_or_keeps_it_keeps_the_ciphertext_byte_for_byte`
the kept column, and
`IntegrationsInfrastructureExtensionsTests.Every_integration_client_has_a_primary_handler_that_follows_no_redirect_and_keeps_no_cookies`
the transport.

## Consequences

Losing or changing the key makes every stored credential unreadable until it is entered again; the
docs say to back up `.secrets/` with `mam.db`, or the variable with the environment. Masking skips
values shorter than 8 characters, so API keys must be at least 8; a qBittorrent password may be
shorter, and no adapter writes a password into a message, so it is exposed only if the service
itself repeats it. A Torznab key still travels in the query string, where the feed reads it, so a
proxy between the app and the feed may log it; only the app's own logs are covered.

## Anti-consequence

Testing an edit with a kept secret sends the stored secret to the address typed in the dialog, before
it is saved: that is what testing an edit means (Vasyl's decision), and only the owner can do it. A
stolen owner session can therefore aim a test at its own host and collect a key the API never
returns. Binding the origin into the associated data would close that; it is left out on purpose.

## Implementation (added 0.1.0, MAM-10)

**A release's download link is the one address an indexer supplies, and the app fetches it only
from the feed's own origin.** Specification §4's warning ("an indexer must not trigger backend
requests to internal addresses") meets its first case here, not in MAM-13 as the Decision expected:
`FetchPayloadAsync` requests an http(s) link only when its scheme, ASCII host and port are the feed's
(an address with user info, or a host without an ASCII form, never is), and sends nothing otherwise.
No redirect is followed, so the rejected "following redirects to the same host" stays rejected; a
redirect to a `magnet:` link is the payload itself, not a request, and every other redirect is refused
with its scheme and host named. The trackers a magnet names (`tr`, `ws`, `xs`, `as`) are fetched by
the download client, not by the app; whether to check them is MAM-13's question.

**The key stays sealed to its integration.** A release's stored link would otherwise hold a copy of
the gateway's key, the very key the Decision keeps only sealed to its row and field: the adapter
stores a placeholder in its place and puts the integration's current key back when it fetches, so a
release row holds no key, a changed key keeps old releases fetchable, and a removed integration's
releases can fetch nothing. The rest of the link, Prowlarr's `link=` token included, is sealed to the
release's own row (`release/{id:N}/payload-locator`), and every part of it is masked in what the feed's
errors echo.

**Text the feed chooses is cut before it is masked.** A credential the app doesn't hold — a tracker
passkey in a URL a gateway's error quotes — can't be masked by value: every address in a feed's error
description is cut to its scheme and host, and every credential-named pair masked, before the text is
cleaned and cut to length (ADR 0004).

`TorznabAdapterPayloadTests.FetchPayload_refuses_a_link_off_the_feeds_origin_without_sending_anything`,
`TorznabAdapterPayloadTests.FetchPayload_redirected_within_the_feed_refuses_too`,
`TorznabAdapterPayloadTests.FetchPayload_redirected_to_a_magnet_hands_the_magnet_over`,
`TorznabAdapterPayloadTests.FetchPayload_fetches_the_torrent_from_the_feed_with_the_key_put_back` and
`TorznabAdapterTests.TestConnection_masks_a_credential_the_error_description_quotes_wherever_its_address_ends`
pin it, and `ReleaseSearchSecretsHttpTests` sweeps the search's answers, the log and the release rows
for every planted credential.

## Implementation (added 0.1.0, MAM-11)

**A qBittorrent session lives between calls now, and is guarded like the password.** The listing of
a download client's destinations keeps the session it signed in for, so a page doesn't sign in on
every load ([ADR 0010](0010-a-destination-is-a-download-clients-own-category-never-a-folder.md)).
It is held in memory only, one per saved integration, beside a SHA-256 fingerprint of the address,
user name and password, each prefixed with its length, so an edited address or credential never
reuses it and the password itself isn't kept; an unsaved draft keeps none. Every session value a call
sends is masked in that call's sentence, the session prints masked (`ToString`), and nothing logs it.
A sign-in qBittorrent refused is kept beside it the same way, as its status and that fingerprint,
never the credentials, so a later call can answer it without sending them (ADR 0010). The connection
test still signs in afresh, never asks whether its credentials were refused and leaves the kept
session alone; when its sign-in works with the very address and credentials refused, it forgets the
refusal, and when qBittorrent refuses it, it keeps that refusal the same way — never in place of one
kept for other credentials.

**"Retry nothing" still holds where it was decided.** The HTTP clients still keep no cookie container
— the adapter sends the one cookie it means to — and have no retry handler, and no call repeats a
failed sign-in, since each refused one counts towards qBittorrent's ban. A sign-in qBittorrent
refused — the credentials, or the app's address banned — isn't sent by a later call either while the
address and credentials stay the same: only once they change or a connection test with them works.
A failure qBittorrent didn't count (unreachable, timed out, a server error) isn't remembered, and the
next call the owner's page makes tries again. The one thing sent twice is a read: qBittorrent
answers a session it has forgotten with 403 (recorded from the reference installation,
`VendorSamples/QBittorrent/expired-session-response.txt`), so a 403 to a kept session drops it, signs
in once more and sends that read again, once. A 403 to a session the call has just signed in for is a
failure, and the session is dropped. No write is ever sent again; MAM-11 sends none.

**Nothing asks for more than it needs.** The listing reads the default save path from
`app/defaultSavePath`, never from `app/preferences`, which holds it too but also carries the Web UI's
API key (5.2) and the proxy and mail passwords — none of which the app should ever receive.

`QBittorrentSessionsTests.Find_tells_apart_credentials_that_run_together_into_the_same_text`,
`QBittorrentSessionsTests.ToString_of_a_session_shows_neither_its_cookie_nor_its_value`,
`QBittorrentAdapterSessionsTests.ListDestinations_signs_in_again_once_the_address_or_credentials_have_changed`,
`QBittorrentAdapterSessionsTests.ListDestinations_of_an_unsaved_draft_signs_in_for_every_call`,
`QBittorrentAdapterSessionsTests.ListDestinations_masks_the_kept_session_and_the_password_in_its_error`
and `QBittorrentAdapterSessionsTests.TestConnection_signs_in_afresh_and_leaves_the_kept_session_alone`
pin the session, `QBittorrentSessionsTests.FindRefusal_once_the_address_or_credentials_have_changed_finds_nothing`,
`QBittorrentSessionsTests.KeepRefusal_of_an_unsaved_draft_keeps_nothing` and
`QBittorrentSessionsTests.ForgetRefusal_with_other_credentials_leaves_the_refusal_kept` the refusal
kept beside it, `QBittorrentAdapterSessionsTests.ListDestinations_whose_renewing_sign_in_fails_does_not_sign_in_again`,
`QBittorrentAdapterSessionsTests.ListDestinations_twice_with_refused_credentials_signs_in_once_and_answers_the_second_from_memory`
and `QBittorrentAdapterSessionsTests.ListDestinations_whose_fresh_session_is_refused_fails_without_signing_in_again`
what is never repeated, `IntegrationsInfrastructureExtensionsTests.A_destination_listing_logs_nothing_from_the_http_client`
and `DestinationsListHttpTests.List_and_get_log_neither_the_address_the_password_the_session_nor_a_sentence`
the log, and `QBittorrentAdapterDestinationsTests.ListDestinations_sends_nothing_but_the_sign_in_and_gets_and_never_a_save_path_or_the_preferences`
the reads.
