# 41. An AI assistant reaches the app through one MCP endpoint for personal access tokens alone, where each tool is a read or one of the three writes, judged per call

Status: accepted (0.5.0, MAM-45)

## Context

Phase E (Vasyl's request of 2026-10-14) lets an AI assistant, asked in plain words, search the
library and the release sources, send the release the user picks and follow its job. ADR 0040
settled what a personal access token may do; MAM-45 settles how an assistant reaches it. The
clients are Claude Code, directly, and Claude Desktop, through the local `mcp-remote` bridge, on the
home network or a VPN (his decision of 2026-10-14), both speaking MCP over Streamable HTTP: every
JSON-RPC message — connecting, listing the tools, calling one — is a POST to one URL.

The obvious answers go wrong here:

- **Mark `/mcp` as any HTTP endpoint.** One POST carries any tool call, and the HTTP level can't see
  which. `[PersonalAccessTokenRead]` would let a `Read only` token's `send_download` through, and a
  signed-in session too; `[PersonalAccessTokenWrite]` would keep a `Read only` token from even
  listing the tools. ADR 0040's Consequences left `/mcp` closed "until it is marked on purpose".
- **The SDK's own authorization.** `[Authorize]` on the tools with `AddAuthorizationFilters()` hands
  the policy an MCP `RequestContext`, never the `HttpContext` `PersonalAccessTokenAccessHandler`
  reads the endpoint from, so the default policy would hide every tool from every token; a named
  policy of its own would be a second copy of ADR 0040's rule.
- **Hide a write and answer a call of it "Unknown tool".** A spike on the SDK did just that, and its
  answer is byte for byte the one a missing tool gets: the user never learns that a `Read and
  download` token would do, and no refusal is recorded, where the HTTP API answers ADR 0040's
  sentence and records it.
- **A tool per HTTP endpoint, as the endpoint is.** `POST /api/releases/search` stores its releases
  under no search, so a send of one of them finds none among the caller's searches and answers that
  the release is gone; and the Download dialog's preselection and its held Send live in the browser,
  so an assistant calling the endpoints would offer what the dialog never would.
- **A session, or OAuth.** A sign-in's access token ends within 15 minutes and carries no access level
  to judge; OAuth is what claude.ai's connectors need, but they call from Anthropic's cloud, so the
  app would have to face the internet over HTTPS — out of scope since 2026-10-14.

The ticket named `ModelContextProtocol.AspNetCore` 1.x; its last release, 1.4.1, gets critical fixes
only, and Vasyl chose 2.2.0 on 2026-10-16, with a twelfth tool, `list_search_releases`, and the
defaults the CHANGELOG lists as put to him.

## Decision

**One endpoint, `POST /mcp`, takes personal access tokens alone — a signed-in session gets 403, no
token or a refused one the API's one 401 —, at most 60 requests a minute per token, and offers
twelve tools, each calling the service the matching web app page calls, as the token's user. A
tool is a read when it says so (`[McpServerTool(ReadOnly = true)]`) and a write only when its method
carries `[PersonalAccessTokenWrite]`, the HTTP API's own mark, which opens it to `Read and download`
alone; a tool that is neither is no token's. The list leaves out what the token can't call, and a
call of it anyway answers ADR 0040's sentence as an `isError` result and is recorded as the HTTP
API records a refusal. The three writes are ADR 0040's three — a send, always Keep both; cancelling
the following of a job; Not interested —, and no tool exists for anything else a token may never
do.**

**The endpoint.** `McpEndpointExtensions.MapMediaAcquisitionManagerMcp` maps `MapMcp("/mcp")` with
the default policy, the new mark `[PersonalAccessTokenOnly]` and `RequireRateLimiting("mcp")`, after
the health endpoints and before the web app's fallback. Stateless (`HttpServerSessionMode.Stateless`,
set explicitly): each POST gets a server of its own with the request's scope, user and
`DbContext`, the token is checked again on every POST (ADR 0002), and nothing is kept between them —
no session or GET event stream to outlive a revoked token, and no server-to-client request, which no
tool needs. Stateless `MapMcp` maps POST alone, so GET, HEAD and DELETE `/mcp` answer 405 `Allow:
POST` where the web app's fallback would answer 200 HTML a client would read as events, and
`/.well-known/{**path}` answers a bare 404 where a client looking for OAuth after a 401 would parse
the page. The server is `mam`, "Media Acquisition Manager", with the build's informational version; it
speaks the `2025-11-25` handshake Claude Code and `mcp-remote` use, and `2026-07-28`.

**Tokens alone.** `[PersonalAccessTokenOnly]` is endpoint metadata, as ADR 0040's marks are:
`PersonalAccessTokenAccessHandler` refuses a session there (`SessionNotAccepted`, 403 "The MCP
endpoint takes a personal access token, not a web app sign-in. Create a token on your Profile page
and connect with it; see “Connect an AI assistant (MCP)” in the README."), with no audit row, as a
session has no token row; and lets a token of either access in once `[DenyPersonalAccessToken]` is
checked, as the tools apply the access themselves. Only `/mcp` carries it.

**The gate, per tool.** `McpToolAccessFilter`, through the SDK's `WithRequestFilters`: the
`tools/list` filter keeps a tool only when the caller's token may call it, and the `tools/call` filter
lets a permitted call through and answers any other with `PersonalAccessTokenErrors`' sentence —
the `ReadOnly` one for a write, `NotForTokens` for a tool that is neither —, recorded through
`IPersonalAccessTokenRefusals` with `tools/call` as its method and the tool as its path: a warning
each time, one `PersonalAccessTokenRefused` row a minute. Writes win over reads, and a name no tool
has goes on to the SDK's own "Unknown tool". A refused call never binds its arguments. `cancel_job`
calls the cancel with `mayEndReplacement: false`, as `DownloadsController.Cancel` does for a token, so
a job whose replacement is still followed is refused with `CancelEndsReplacement` and recorded.

**The pages' services, as the token's user.** Each tool takes the caller as its `ClaimsPrincipal`, and
the services scope by its user id, so a member's assistant reads that member's jobs and searches only
and what only the owner reads stays the owner's. `search_releases` makes the Search page's stored
search (`ISearchService`), whose releases a `Read and download` token of the same user can send, and
`list_search_releases` reads it again filtered, sorted and paged without asking the sources (Vasyl's
twelfth tool). `get_download_options` answers what the dialog opens on, through
`DownloadDialogPreselection`, the C# twin of the dialog's `preselect()` and `destinationOf()`, and the
overwrite check of that destination; `send_download` refuses a new send beforehand, writing nothing,
where the dialog disables Send — a folder the client can't see —, with the dialog's own sentence,
while a retry under a `requestId` that made a job is answered with that job first, asking no client,
as the send answers it whatever changed since (ADR 0013). `get_status` is the "who am I" ADR 0040 left
to MAM-45: the user, the calling token, the version, the library sources, the latest recommendations
run and the failed release sources, never Settings' health. `list_duplicates` takes a new sort,
`Recoverable`, on the HTTP API too (ADR 0028's MAM-45 section).

**The user decides a send.** `send_download` alone carries `anthropic/requiresUserInteraction` in its
listing, so Claude Code asks before every send whatever its rules allow; the instructions, sent per
access on connecting (`McpInstructions`, under 2,000 characters), tell the model to send only what the
user confirmed, with the same `requestId` on a retry (ADR 0013). Every tool also sets its four hints,
but they are advice a client shows, never the gate.

**Answers.** `McpToolResults`: a success is a one-sentence summary and the data as JSON, in a text
block and as `structuredContent` matching the tool's output schema; an expected failure is the
sentence alone, `isError`, returned and never thrown, as the SDK would answer a thrown one "An error
occurred invoking '…'." and log it at Error. `McpArgumentErrorFilter`, the outermost, answers
arguments that don't fit with "The arguments don't fit `<tool>`: `<reason>` Check them against the
tool's input schema and call it again.", for what the binder throws alone — told apart by the stack,
as the same call runs the tool —, so the same exceptions from a tool that ran stay the SDK's error,
logged at Error. `McpJson` makes a fresh options instance per host — nulls written, as Claude Code's
SDK checks the content against the schema; instants with `Z`; enums by name; Cyrillic unescaped —
shared by the binding, the schemas and the answers. An instant's schema is a `date-time` string
(nullable where the property is), which `McpToolRegistration` writes from each object's properties:
the exporter can't see through the API's converter and writes `true`, and the MCP TypeScript SDK
that Claude Code and `mcp-remote` run on refuses the whole `tools/list` answer for one property
schema that isn't an object — found by the live check of 2026-10-16, which the SDK's C# client
doesn't catch.

**60 a minute per token.** `McpRateLimitPolicy` partitions by the token's id, so a looping assistant
or a leaked token is stopped within the minute while the user's other tokens and everyone behind the
same router go on, and answers 429 "Too many requests with this access token: at most 60 a minute.
Wait a minute and try again.". A wrong token never gets here: it is challenged first, and counts
against its address's sign-in limit (ADR 0040).

| Rejected option | Why not |
| --- | --- |
| `/mcp` marked `[PersonalAccessTokenRead]` | One POST carries any tool: a `Read only` token's send, and a session, would pass |
| `/mcp` marked `[PersonalAccessTokenWrite]` | A `Read only` token couldn't even list its tools |
| The SDK's `[Authorize]` and `AddAuthorizationFilters()` | The policy never sees the `HttpContext`, so the default policy hides every tool; a named one copies ADR 0040's rule |
| A refused write answered "Unknown tool" | Says nothing the user could act on, and records no refusal, unlike the HTTP API |
| A sign-in's access token on `/mcp` | It ends within 15 minutes and carries no access to judge, nor a token row to record on |
| OAuth for claude.ai's connectors | They call from Anthropic's cloud: the app would have to face the internet over HTTPS |
| Stateful sessions | A session and its event stream could outlive the token's revocation, for nothing a tool needs |
| `search_releases` through `POST /api/releases/search` | Its releases belong to no search, so none of them could be sent |
| A limit by address or by user | One assistant's loop would hold up the household, or the user's other assistants |
| `ModelContextProtocol.AspNetCore` 1.x | 1.4.1 is the last 1.x and gets critical fixes only |
| The SDK's `WithToolsFromAssembly` | Hosts built in parallel could get the caller listed as a required `user` argument; `McpToolRegistration` leaves it out by type |
| `get_status` with Settings' health | No token reads Settings (ADR 0040) |

Each decision is pinned. The endpoint, in `McpEndpointHttpTests`:
`Post_without_a_usable_token_is_the_same_401_the_api_gives_byte_for_byte`,
`Post_with_refused_tokens_uses_up_the_sign_in_attempts_of_their_address_then_every_token_from_it_is_429`,
`Post_with_a_sessions_access_token_is_403_with_the_sentence_and_no_audit_row`,
`Method_other_than_post_is_405_allowing_post_with_an_empty_body`,
`Get_of_oauth_discovery_is_a_bare_404_not_the_spa_shell`,
`Post_with_a_token_reaches_the_mcp_handler_with_or_without_the_trailing_slash`,
`Post_beyond_sixty_a_minute_with_one_token_is_429_with_the_mcp_sentence_while_another_token_gets_through`,
`Initialize_introduces_mam_with_its_title_and_the_builds_version`,
`Connecting_on_either_protocol_gives_each_access_instructions_that_name_what_it_may_do`,
`Client_on_claude_codes_protocol_or_the_sdks_latest_connects_and_lists_the_tools` and
`Token_secret_appears_in_no_log_entry_whatever_the_endpoint_answers`; the mark on a probe endpoint,
`PersonalAccessTokenConventionHttpTests.Post_marked_for_tokens_alone_with_a_token_of_either_access_is_2xx`
and `Post_marked_for_tokens_alone_with_a_session_is_403_with_the_sentence_and_records_nothing`.

The lists, in `McpToolListHttpTests`:
`List_with_a_read_only_token_is_exactly_the_nine_reads_for_the_owner_and_a_member`,
`List_with_a_read_and_download_token_is_exactly_the_twelve_tools_for_the_owner_and_a_member`,
`List_marks_each_tool_read_only_or_not_with_its_title_and_every_hint_set`,
`List_asks_for_the_users_interaction_on_send_download_and_no_other_tool`,
`List_gives_every_tool_a_description_an_object_output_schema_and_a_description_of_every_parameter_but_no_caller`,
`List_describes_every_property_of_every_schema_as_an_object_and_a_time_as_a_date_time_string`
and `Discovery_finds_exactly_the_twelve_tools_each_a_read_or_one_of_the_three_writes_and_never_both`,
which keeps the lists fixed by reflection, so the test guards itself. The gate, in
`McpToolAccessHttpTests`:
`Call_of_a_write_with_a_read_only_token_is_an_error_with_the_read_only_sentence_and_changes_nothing`,
`Refused_calls_of_one_token_log_a_warning_each_and_make_one_audit_row_a_minute`,
`Call_of_a_read_with_a_read_only_token_passes_the_gate_and_records_nothing`,
`Call_with_arguments_that_dont_fit_is_an_error_saying_what_to_fix`,
`Call_whose_tool_throws_an_argument_or_json_exception_of_its_own_is_the_sdks_error_logged_as_an_error` and
`Call_of_a_tool_no_one_has_is_the_sdks_unknown_tool_error_and_records_nothing`; the refused cancel,
`McpJobToolsHttpTests.CancelJob_of_a_job_whose_replacement_is_followed_is_refused_with_the_sentence_changes_nothing_and_is_recorded`.

The pages' services: `McpLibraryToolHttpTests.SearchLibrary_answers_what_the_library_page_lists_for_the_same_user_with_path_problems_for_the_owner_only`;
`McpReleaseToolsHttpTests.SearchReleases_stores_the_search_its_sources_and_its_releases_as_the_search_page_does`,
`SearchReleases_finds_releases_a_read_and_download_token_of_the_same_user_sends_through_the_http_api`,
`ListSearchReleases_pages_the_stored_search_without_asking_a_source_again` and
`ListSearchReleases_of_another_users_search_is_the_not_found_sentence_even_for_the_owner`;
`McpDownloadToolsHttpTests.GetDownloadOptions_preselects_the_suggested_destination_in_the_client_that_holds_it_and_checks_it`,
`GetDownloadOptions_with_one_client_of_several_destinations_and_no_suggestion_preselects_the_client_alone`,
`SendDownload_to_a_folder_transmission_can_not_see_is_refused_before_anything_is_written_fetched_or_sent`,
`SendDownload_sends_the_release_as_its_user_keep_both_and_answers_the_job` and
`SendDownload_again_with_the_same_request_id_answers_the_same_job_and_sends_nothing_twice` and
`SendDownload_again_with_the_same_request_id_after_transmission_lost_sight_of_the_folder_answers_the_job_it_made_and_asks_no_client`;
`McpJobToolsHttpTests.ListJobs_of_a_member_lists_and_counts_only_their_own_jobs` and
`GetJob_of_another_users_job_is_the_error_a_missing_one_gets`;
`McpStatusToolHttpTests.GetStatus_with_a_members_token_names_the_member_and_a_token_that_never_expires_by_name_and_with_nulls_written`
and `GetStatus_writes_its_json_text_with_cyrillic_as_it_is`;
`McpDuplicateToolHttpTests.ListDuplicates_sorted_recoverable_lists_the_most_room_first_and_the_groups_without_an_estimate_last`;
and `McpRecommendationToolsHttpTests.DismissRecommendation_marks_the_work_for_the_caller_with_no_audit_row_and_it_leaves_their_list_only`.
In the web app, `CreateAccessTokenDialog.test.tsx`'s "gives the Claude Code command with the whole
token, to run once in a terminal" and "gives the Claude Desktop configuration through mcp-remote, the
header without spaces and --allow-http on a plain-HTTP address", and
`CreateAccessTokenDialog.https.test.tsx`'s "points both Claude lines at the HTTPS address and leaves
--allow-http out of the Claude Desktop configuration" pin the lines Profile gives.

## Consequences

An assistant with a token does what a script with it may, as its user, at one URL, and its user
connects it with one line from Profile. A tool added later needs a decision: `ReadOnly = true` or the
write mark, and its name in `McpToolListHttpTests`' lists, or the discovery test fails; a tool with
neither is hidden from every token and refused. A write the HTTP API doesn't open to tokens has no
business getting the mark here.

What is left open on purpose. claude.ai and the phone app can't connect. The token sits in plain
text in `~/.claude.json` or `claude_desktop_config.json` and crosses a plain-HTTP network as it is,
as ADR 0040 says of every token. Whether Claude Desktop, through `mcp-remote`, honours
`requiresUserInteraction` isn't documented; the README says to leave its **Allow always** off for
`send_download`. No tool reports progress; the SDK starts the answer before the tool runs, so Claude
Code's 60-second wait for a server's first byte isn't reached by a slow search, and the README names
`MCP_TOOL_TIMEOUT` for a client that waits less. A service's sentence goes out as it is, some in the web app's words ("Reload
the page and try again."), and in English until MAM-47. The dialog's preselection now lives twice,
each pinned by its own side's tests and nothing comparing them; a change to one belongs in the other
(`DownloadDialogPreselection` says so). On 2026-10-16 the MCP TypeScript SDK's client (1.32.1, what
Claude Code and `mcp-remote` run on) called every tool against the app on real Kestrel with canned
data, a send included, and `mcp-remote` 0.15.1, started with the Profile block's arguments, carried a
Claude Desktop's stdio through to it; Claude Code and Claude Desktop themselves haven't met it yet:
MAM-48's acceptance bar does that.

## Anti-consequence

This doesn't license a tool for anything ADR 0040 keeps from every token — a replacement, the
quarantine, a copy's removal, a setting, a user, a token, undoing Not interested —, a write tool
without the mark, `[PersonalAccessTokenOnly]` on an endpoint that doesn't judge each call itself, or a
session on `/mcp`. The hints and the instructions are advice to the client and the model; the gate
is the filter, and nothing else may stand in for it.
