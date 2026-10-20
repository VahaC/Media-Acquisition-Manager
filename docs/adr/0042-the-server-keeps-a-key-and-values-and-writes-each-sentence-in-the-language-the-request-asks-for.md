# 42. The server keeps a key and values and writes each sentence in the language the request asks for

Status: accepted (0.5.0, MAM-47). Amends [ADR 0001](0001-every-ui-string-comes-from-a-locale-resource.md).

## Context

After MAM-46 the web app speaks Ukrainian, but everything the server writes still reads English on a
Ukrainian page: an API error, a job's or a check's reason, a run's failure, a disabled button's
reason, a connection test's answer, a health description, the MCP tools' answers. ADR 0001's MAM-46
section left them English "until MAM-47, whose ADR amends this one". They are about 850 sentences,
made by some forty classes in Core, Infrastructure and the Api, many of them glued from fragments
("the old copy", "a .torrent file", "Two"…"Ten"), and 21 columns store them.

The obvious answers go wrong here:

- **Translate where the sentence is made.** Most reasons are written by background work — the job
  poller, a replacement's verifier and mover, the syncs, scans and inspections, the reconcilers —,
  which has no request and no reader. A reason stored in the language of whoever caused it shows a
  member's English on the owner's Ukrainian page, and a stored sentence can't be shown in another
  language.
- **A column per language, or key and values beside the sentence.** Twice the 21 columns, an older
  image still reading its English one, and a third language would migrate every table again.
- **The account's language, `Users.Language`.** A 401, a sign-in failure, a token refusal and a 429
  come before anyone is known; reading the account there would make an unknown user's answer differ
  from a known one's, the probe ADR 0002 and ADR 0040 close with one identical answer. And a server
  sentence must never differ from the page around it.
- **Leave the stored English as it is.** A job stopped before the update would read English for
  good in the Ukrainian app; Phase E's acceptance bar names that very case.
- **SQL in a migration to convert it.** The house keeps EF's own operations alone in a migration
  (R22), and SQL can't read a sentence that nests another or quotes a path holding `”`.

## Decision

**Core, Infrastructure and the database keep no sentence: they make a message — a key and its
values — and the Api writes it when it answers, in the language the request's `Accept-Language` asks
for, English or Ukrainian, else English, from `src/MediaAcquisitionManager.Api/Locales/<language>/`.
Every API error answers `{ "error": "<sentence>", "code": "<key>" }`, the key the same in every
language and release. The 21 sentence columns hold the key and values as JSON in place, and the
English an older build stored is converted as the app starts.**

**A message.** `Core/Texts/Message`: a key — lower-case kebab segments, the first naming its file,
such as `downloads.send.already-sent` — and named values: text (a name, a path, an id, never
words of ours), a service's own words (`VendorText`, made only by cleaning and masking them, as
`RemoteText.Clean` did), a number, a size, a running time, a list, or another message. The classes
that built strings build messages under the same names — `DownloadErrors`, `FileInventoryMessages`,
every `*Rules.Validate…` —, and Core still references no package. A key is always one whole string
literal, so a test sees every key the code names.

**The files.** One file per area and language, in i18next's shape as the web app's: nested objects,
`{{name}}`, plural forms on `count` (`_one`/`_other` in English, `_one`/`_few`/`_many`/`_other` in
Ukrainian), and a hint on every value that isn't text — `{{count, number}}`, `{{said, vendor}}`,
`{{error, message}}`, `{{paths, list:and}}`, `{{limit, size}}`, `{{new, duration}}`, and
`{{work, name}}` for a slot that holds a title or, for a work with none, the label "a work with no
title" in the reader's language. A fragment has a key of its own only when it is a whole sentence
or a clause that agrees with nothing around it; anything else is a whole sentence per shape, so a
translation places its own words. Quotes belong to the template, “…” in English and «…» in
Ukrainian. The files, about 970 keys in 20 areas a language, are embedded in the Api, so the image,
`dotnet run` and the tests read the same bytes. The English templates reproduce each sentence as it
was, byte for byte, the hints carrying each call site's format (`0.##` seconds, `N0` byte counts,
`Two`…`Ten`, the `1:56:30` clock), so the suite's English assertions, about 1,600, stayed its
regression net. `docs/i18n/glossary-uk.md` binds the Ukrainian, its "Server sentences" section
included.

**The request's language.** `RequestLanguage.Of` orders the `Accept-Language` ranges by quality,
reduces each to its primary subtag (`uk-UA` is `uk`) and takes the first of `en` and `uk`; none is
English. The web app sends its UI locale on every request (ADR 0001's MAM-46 section), so a server
sentence reads in the language of the page around it, and a switch on Profile fetches again what is
on screen (`lib/query-client.ts`). A script, a token or an MCP client gets English unless it sends
the header. `Users.Language` isn't read (Vasyl's decision of 2026-10-19), so the 401 of every cause,
the sign-in failures and the token refusals stay one answer per header, byte for byte. No
`CultureInfo` changes and no `RequestLocalization`.

**Only the Api writes words, when it answers.** Mappers and the services that build a DTO's sentence
render through `IRequestTexts`, a singleton over the request's `HttpContext`, so the mappers stay
singletons; the MCP tools render their summaries and errors, and the health writer its descriptions.
Background work stores the message and never renders it. Logs stay English (`texts.English`), so an
operator reads one language. In production the renderer never throws: a key the Ukrainian file lacks
reads in English, a key this build doesn't know reads "This message was written by another version
of the app and can't be shown here (<key>).", a missing value "…", each logged once as a warning; in
the tests it throws.

**Errors carry their key.** One writer, `ApiErrorBody`, writes every error body: the controllers'
helpers, the sign-in challenge's 401 (`account.sign-in-required`), the owner-only 403
(`account.owner-only`), a token's 403 and 429, the exception handler's 500, 413 and 400, and the
unknown endpoint's 404. **A key a column may hold is never renamed or removed**: once no code makes
it, its template stays and the key goes into `tests/MediaAcquisitionManager.Api.Tests/Texts/retired-keys.txt`,
empty in 0.5.0. The framework's `ValidationProblemDetails` for a body or a query parameter the binder
can't read stays as ASP.NET writes it, in English and without a code (Vasyl's decision of
2026-10-19): only a script meets it, and the web app never shows it. Four DTO fields carry the key
beside their sentence — a library source's `pathProblemCode`, its inspection's `problemCode`, a TMDb
dialog item's `cannotWriteReasonCode` and `writeErrorCode` —, as the web app compared those
sentences by their words (`LibrarySyncPanel`, `TmdbLinkEditor`).

**The database keeps no sentence.** The 21 columns, `DownloadJobs.Error` to `RecommendationRuns.Error`,
keep their name, TEXT type, `[MaxLength(1000)]` and nullness, and hold canonical JSON —
`{"k":"replacements.move.quarantine-failed-tally","v":{…}}`, Cyrillic unescaped — through one EF value
converter registered by convention for every `Message` property, so the model doesn't change and no
migration rebuilds a table (ADR 0009). A message longer than its column is fitted by cutting its
longest value with "…", so the sentence keeps its own words around the cut. A column that holds no
message JSON — a row an older image wrote after a rollback, or one the converter couldn't read —
reads as `common.legacy-text`, its words shown as they are. Code that compared stored sentences
compares keys (`link.WriteError?.Key == NoEpisodeImdbKey`) or whole messages (the job poller's
"unchanged", fitted as the column keeps it).

**Stored English is converted at start.** `StoredTextConverter` runs in C# after `Migrate()` and
before any hosted service starts, the reconcilers and workers included. It finds the 21 columns from
the model and reads the rows that aren't a message yet — a null or an empty text is left as it is —
in pages of 500 by `rowid`, each page written in one transaction. It parses each text with the
English templates themselves, plus `Locales/older-en.json` for a wording an older build stored that
today's templates no longer hold: a backtracking parser that accepts a reading only when rendering
it again gives the stored text byte for byte — or, for a sentence an older build cut at its
column's 1,000 characters inside its last value, a text that begins with it. A row it can't read
keeps its words, marked with the converter's version (`StoredTextConverter.Version`, 1), so a later
build that learns the wording tries it again. A row is written only while it still holds the text
read (`UPDATE … WHERE rowid = @id AND <column> = @old`), a converted row is never read again, and a
second start changes nothing. Nothing stops the boot: a column that fails is logged by its name,
the pages it hadn't finished stay as they were and are tried again at the next start, and the other
columns are converted. The log says per column how many texts it converted, cut, shortened and
kept, and which row ids it kept, never a text. Before the release it ran on a consistent copy of
production's database, taken on 2026-10-20 (`tools/mam47/`, run by Vasyl): of 26 texts, 22 stored
sentences in seven of the columns read back exactly and the 4 audit rows of his confirmations were
named by their claim; none was kept as its words, cut or shortened, so `older-en.json` is empty in
0.5.0, and every converted row renders in Ukrainian too.

**The owner's confirmation.** What the owner ticks in the Download dialog ("This release is Alien
(1979), season 2") is kept as the claim it is — the work, its seasons and episodes, the copy or
place that tells twins apart — in `ReplacementJobWorks.ConfirmationText`, and reads in the reader's
language. The audit row can't keep the sentence, so `AuditEvents.TargetName` names the claim in no
language — `Alien (1979)`, `Doctor Who (2005) S02E01–E05 · Фільми`, `— S01` for a work with no title —
through `ReplacementConfirmationLabel`, and the converter rewrites older audit rows the same way
(Vasyl's decision of 2026-10-19). Which tail tells twins apart is measured in English, so the choice
never changes with the reader's language.

**Other programs' words and labels.** What a service, the operating system, ffprobe or .NET's binder
said goes into the reader's sentence verbatim, masked of the secrets used. A menu or button of
another product named in a Ukrainian sentence is that product's own Ukrainian label where it has a
Ukrainian interface — Jellyfin's and qBittorrent's, read from their translation files at a recorded
release —, and its English label in «…» where it has none, Transmission's web interface and Jackett
(Vasyl's decision of 2026-10-19). The app's own labels are the web app's, exactly. HTTP status
phrases, API names and README headings stay as they are.

**AI-facing text stays English.** The MCP server's instructions and its tools' titles and
descriptions are read by the model, which answers the user in their language anyway. The tools'
answers — summaries, refusals, a job's reason — follow the request's `Accept-Language` like any
API answer.

**Numbers.** English writes them as each sentence did; Ukrainian as the web app's `lib/format.ts`
(`1 234`, `0,5`), sizes by their exact unit (`8 МіБ`), running times as the server's clock in both
(`0:45:00`), and a count that opens a sentence as digits.

**No sentence escapes the files, by test.** `run-tests.sh` runs the whole .NET suite twice by
default, Jenkins included (`PSEUDO=0` skips the second pass for a quick local run): the second pass
sends every request in a pseudo-language whose sentences the server wraps in ⟦…⟧, which the harness
unwraps where the English assertions read them, and it fails a DTO field holding a sentence that the
harness's list doesn't name, a rendered sentence written to the database and a rendered sentence in
a log. An IL scan of the three assemblies refuses a literal sentence that reaches a reader, a
literal made into a message's value, a message printed into a string or handed to a log as an
argument, and — in the test projects too — a message compared with a string; every key the code
names has its English template and every template is named; every English file has its Ukrainian
one, which holds to it by the web app's parity rules and the server's; every label quoted in
Ukrainian is the web app's or a recorded vendor's; and every English wording reads back as its key
and values.

| Rejected option | Why not |
| --- | --- |
| Render in services, or when a reason is stored | Background work has no reader, and a stored render is a sentence in the database |
| `Users.Language` for the server's words | Vasyl's decision; a sentence would differ from the page around it, and a 401 would tell a known user from an unknown one |
| `RequestLocalization`, a culture per request, `.resx` or ICU MessageFormat | A second plural convention beside the web app's i18next, and a culture that reaches every format |
| New key and values columns beside the sentence | Twice the columns, an older image reading English, a third language migrating every table |
| Columns kept `string`, with a codec at every writer | Nothing would stop a writer storing English or a rendered sentence |
| A rendered-text type in the DTOs | Its converter makes the MCP schema `true`, and the TypeScript SDK refuses the whole `tools/list` (ADR 0041) |
| A source generator, or a catalogue of typed factories | About forty types of machinery for what strict rendering and the round trip prove for every sentence a test reaches |
| `PRAGMA user_version` for the conversion | The house's precedent is a version per row (`CatalogStore.CurrentDetailsVersion`) |
| Scoped mappers | A change of lifetime everywhere for what a singleton over `IHttpContextAccessor` does |
| A regular-expression converter | No backtracking across nested sentences; sizes and times would read back as text |
| `{ error, code }` for the binder's failures | Not wanted (Vasyl, 2026-10-19): scripts alone meet them, and they keep the framework's shape |
| A sweep of the database after every test | A scan per test that blames the next one; the command interceptor fails the test that wrote |

Each decision is pinned. The request's language, in `RequestLanguageHttpTests`:
`Unmatched_route_answers_in_the_language_the_header_ranks_first`,
`Unmatched_route_ignores_the_language_a_user_saved` and
`Owner_only_refusal_ignores_the_language_the_member_saved`; one answer per cause in Ukrainian too,
`AuthSignInHttpTests.SignIn_failures_asked_in_ukrainian_are_the_identical_401_with_the_invalid_credentials_code`
and `PersonalAccessTokenAuthenticationHttpTests.Get_with_any_refused_credential_asked_in_ukrainian_is_the_identical_401_with_the_sign_in_code`.
The code, `SpaFallbackHttpTests.Unknown_api_route_answers_the_sentence_and_its_code`,
`UnreadableBodyErrorAttributeHttpTests.Update_language_with_a_body_mvc_cant_read_answers_the_sentence_with_its_key_as_the_code`
and `ApiErrorBodyTests.For_takes_the_messages_key_as_the_code`. The renderer, in
`MessageRendererTests` (the fallbacks
`Render_lenient_writes_english_for_a_key_missing_from_a_ukrainian_file_that_exists_and_says_so`,
`Render_lenient_writes_that_another_version_wrote_a_key_this_one_has_not`, the strict throws,
`Render_inserts_a_value_as_it_is_without_reading_it_as_a_template`, and the name slot's
`Render_writes_the_label_of_a_thing_with_no_name_in_the_same_language`), `RequestTextsTests`,
`PluralRulesTests`, `LanguageFormatsTests` and `EnglishFormatPinTests`, one test per call site whose
format moved into a hint. The columns, in `MessageColumnsTests`
(`Every_message_column_is_a_message`, `Every_message_column_stays_the_text_column_of_1000_characters_it_was`,
`No_other_column_holds_a_message`),
`ModelHasNoPendingChangesTests.The_model_matches_the_last_migrations_snapshot` and `MessageColumnTests`
(`A_column_holding_no_message_json_reads_as_its_words`,
`SaveChanges_fits_a_message_longer_than_the_column_by_cutting_its_longest_value`,
`Where_compares_a_column_with_a_message_constant_through_the_converter`). The conversion, in
`MessageRoundTripTests.Every_english_wording_reads_back_as_its_key_and_values` and
`Every_older_wording_reads_back_as_its_key_and_values`, `StoredEnglishCorpusTests`
(`Every_corpus_text_reads_as_expected`, `Parse_reads_a_text_cut_inside_its_last_value`,
`Parse_keeps_a_text_cut_anywhere_but_inside_its_last_value`,
`Parse_reads_a_plural_slip_with_the_form_it_was_written_in`), `StoredTextConverterTests`
(`Convert_turns_the_older_english_of_every_message_column_into_its_key_and_values`, every column of
the model and the audit column planted,
`Convert_writes_back_the_very_words_that_were_stored_when_the_message_is_rendered_in_english`,
`Convert_of_a_text_cut_at_the_column_limit_inside_its_last_value_converts_it_with_that_value_cut_and_fitted`,
`Convert_keeps_a_text_it_cant_read_back_as_its_words_marked_with_its_version`,
`Convert_tries_again_a_text_kept_under_an_older_version_and_leaves_one_kept_under_its_own`,
`Convert_run_again_changes_nothing`, `Convert_leaves_null_empty_and_message_like_texts_as_they_are`,
`Convert_failing_in_the_middle_of_a_page_keeps_the_pages_before_it_and_every_row_readable`,
`Convert_after_a_run_that_failed_in_the_middle_of_a_page_converts_the_rest`,
`Convert_logs_a_column_whose_read_fails_and_converts_the_others`,
`Convert_logs_counts_and_row_ids_and_never_a_text`,
`Convert_names_an_older_builds_confirmation_audit_row_by_its_claim`,
`Convert_reads_a_work_with_no_title_back_as_its_label_in_a_name_and_in_a_list_of_names` and
`Convert_of_the_links_older_english_gives_the_keys_the_repair_and_the_writer_rely_on`),
`StoredTextConverterStartupHttpTests`
(`Start_converts_the_older_builds_english_before_the_host_answers_a_request`,
`Start_with_a_column_whose_read_fails_logs_it_converts_the_others_and_serves`,
`Jobs_list_in_ukrainian_reads_the_reasons_an_older_build_stored_in_english` and
`Catch_up_leaves_out_the_link_an_older_build_marked_as_restarted`), and
`ProductionCopyConversionTests.Convert_on_a_copy_of_production_writes_back_every_row_it_converts_and_reports_the_rest`,
skipped unless `MAM47_PRODUCTION_DB_COPY` names a copy. The confirmation,
`ReplacementConfirmationLabelTests.Of_says_the_claim_in_todays_english_and_its_label_in_no_language`
and `Of_an_older_builds_words_read_back_as_their_claim_gives_the_same_label`, and
`ReplacementOptionsHttpTests.Options_tells_twins_apart_by_a_tail_measured_in_english_whatever_language_the_page_asks_for`.
The proofs, in `NoLiteralSentenceTests` (`No_literal_sentence_reaches_a_reader`,
`No_constant_sentence_waits_for_a_reader`, `No_literal_becomes_a_messages_value`,
`No_message_is_printed_into_a_string`, `No_message_is_compared_with_a_string`,
`Only_the_json_codec_and_the_stored_text_parser_make_stored_vendor_words`,
`The_scan_flags_the_probes_sentences_and_spares_its_log_and_exception`), Core's and
Infrastructure's `NoMessageComparedWithAStringTests.No_test_compares_a_message_with_a_string`,
`KeysInUseTests`
(`Every_key_named_in_code_has_an_english_template`, `Every_english_key_is_named_in_code_or_retired`,
`Every_retired_key_keeps_its_english_template`), `LocaleParityTests`
(`Problems_answers_the_web_apps_own_parity_cases`, `Every_english_template_follows_the_server_rules`,
`Every_ukrainian_file_holds_to_its_english_file`, `Every_english_file_has_its_ukrainian_file`),
`ServerLabelsTests` (`Every_label_quoted_in_a_ukrainian_template_is_the_web_apps_or_a_recorded_one`,
`The_servers_edition_labels_read_as_the_web_apps`,
`Every_server_edition_label_is_paired_with_the_web_apps`),
`PseudoSentenceFieldsTests.Every_contract_field_named_like_a_sentence_is_listed_as_a_sentence_or_as_data`,
`PseudoWriteGuardTests.Refuse_fails_a_parameter_holding_a_rendered_sentence`,
`PseudoLogGuardTests.Log_of_a_rendered_sentence_fails_in_the_pseudo_pass_only` and `PseudoPassHttpTests`.
What a Ukrainian reader sees, one class per area spelling the Ukrainian it expects, and English
without the header: `AuthUkrainianHttpTests`, `ProfileUkrainianHttpTests`,
`DownloadsUkrainianHttpTests`, `SearchUkrainianHttpTests`, `DestinationsUkrainianHttpTests`,
`IntegrationsUkrainianHttpTests`, `LibraryUkrainianHttpTests`, `LibraryLinksUkrainianHttpTests`,
`DuplicatesUkrainianHttpTests`, `ReplacementsUkrainianHttpTests`,
`RecommendationsUkrainianHttpTests`, `McpUkrainianHttpTests` and `HealthUkrainianHttpTests` —
among them one Ukrainian body for every cause of a sign-in failure and of a 401
(`AuthUkrainianHttpTests.SignIn_failures_asked_in_ukrainian_are_one_ukrainian_body_for_every_cause`,
`Request_with_a_refused_credential_asked_in_ukrainian_is_one_ukrainian_body_for_every_cause`), a
reason an older build stored in English, converted by the real converter and read in Ukrainian
(`DownloadsUkrainianHttpTests.List_asked_in_ukrainian_reads_a_block_reason_an_older_build_stored_in_english`),
a reason kept as its words shown as they are
(`List_asked_in_ukrainian_shows_a_reason_kept_as_its_words_as_they_are`), the owner's confirmation
as the claim in Ukrainian
(`ReplacementsUkrainianHttpTests.Options_asked_in_ukrainian_word_the_owners_confirmation_as_the_claim_in_ukrainian`)
and the tools' descriptions kept English
(`McpUkrainianHttpTests.List_tools_on_a_connection_asking_for_ukrainian_keeps_the_descriptions_for_the_model_in_english`).
In the web app, `query-client.test.ts`'s "refetches
the cached answers on screen when the UI language changes, so the server's sentences follow it",
`TmdbLinkEditor.test.tsx`'s "calls a save done when the kept reason has the code the item showed
before, though its words changed" and `LibrarySyncPanel.test.tsx`'s "does not quote an unreadable
file's problem of the same code as the path problem, though it names another file".

## Consequences

A correction to a Ukrainian sentence is an edit of `Locales/uk/<area>.json`, no code. A new sentence
is a key in its area's English file and the Ukrainian one, or the key and parity tests fail; a
sentence nobody can read is deleted, not translated. A third language is a folder of files, a
`TextLanguage` with its plural rules and number formats, and its code in `RequestLanguage`.

English changed only where it had to, each change recorded with the test that pins it (MAM-47's
ledger). What a reader can meet: "1 byte" for one byte where a new file's size reason said "1
bytes"; "1 different movie" (or episode) where a sync's failure said "1 different movies"; a
removal's name found swapped on Restore says "this copy's own file", no longer a replacement's "the
old copy's"; a work with no title loses its year in the consent words and the plan's refusals ("This
release is a work with no title, season 2"), while the pairing's reasons keep it; a sentence longer
than its column is cut inside its longest value, so its closing words stay; the audit row of an
owner's confirmation names the claim, not his sentence; and every error body gains `code`. What no
reader meets today: "1 second", "1 minute" and "1 byte" where a timeout or a limit of exactly one
said the plural, and the `_one` forms of limits no constant of today reaches; an explanation of the
operating system's or ffprobe's ending in " ." loses the space; .NET's binder message in the MCP
arguments' sentence is cleaned like a service's words; a progress that isn't a number is left out of
`get_job`'s summary; and the adapters' guards against a caller's bug name the rule they enforce
instead of quoting a reader's sentence. Sentences no page or answer could show were deleted, not
translated. Every other sentence is byte-identical.

The test phase runs twice: the second pass adds about 65 to 100 seconds, so a full `run-tests.sh`
takes about three minutes where the first pass alone takes about one and a half (77 s alone, 164 s
for both with the build up to date, measured on 2026-10-20).

**Rolling back to an older image** (accepted by Vasyl on 2026-10-19) shows every converted reason as
its raw JSON in that image's pages and API, and more:

- that image's catch-up compares `LibraryItemLinks.WriteError` with its English restart sentence, so
  a converted restart row counts as a repair candidate there, and **Update Jellyfin** may send a second
  Apply to an item whose earlier write was interrupted: after a rollback, wait a few minutes before
  pressing it;
- it doesn't know a converted "TMDb knows no IMDb id of this episode" row, so it asks TMDb again for
  those episodes and writes English reasons — it heals itself;
- its job poller sees every followed job's `BlockReason` as changed and writes it once again, in
  English;
- `ReplacementJobWorks.ConfirmationText`, which no page of it shows, holds JSON, and the twins are
  worked out afresh;
- upgrading again converts whatever English the older image wrote, as at the first start.

What is left open on purpose. A value whose name is misspelt in a `Message.Of` no test reaches shows
"…" in production, with one warning in the log. Health descriptions and MCP tool errors carry no
code (an MCP `_meta` code is an offer). The one stored time of day stays "after 14:05 UTC", in no
reader's time zone. A key renamed later needs an alias in the reader, not built while no key is
renamed. The web app shows `error` and doesn't route a 400 to its field by `code` (an offer).

## Anti-consequence

This doesn't license storing a rendered sentence anywhere — a column, a cache, an audit name —,
rendering in background work, translating a service's own words or a title, reading the account's
language for a server sentence, or renaming or removing a key a column may hold. Logs, the release
notes, the `index.html` title and AI-facing MCP text stay English by this decision, not by oversight.
