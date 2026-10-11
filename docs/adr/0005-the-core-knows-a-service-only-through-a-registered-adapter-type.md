# 5. The core knows a service only through a registered adapter type, never by its product

Status: accepted (0.1.0, MAM-7)

## Context

Specification §2 keeps the core independent of any one media server, indexer or download client,
and the acceptance bar of Phase D asks for a second search or client adapter "without changing the
core state machine", with error handling and the UI not depending on qBittorrent-specific fields.
MAM-7 stores the first integrations — Jellyfin, a Torznab feed (Prowlarr serving Toloka, ADR 0004)
and qBittorrent — so each row now has to say which code talks to it, and the core needs contracts
for the libraries, release sources, download clients, file inventory and metadata of later tickets.

The obvious shapes each leave something behind. A product enum in Core puts product names into the
core and makes every new adapter a Core change and a migration. A table per product multiplies the
migrations, the settings page and every query that lists integrations. A JSON settings bag per row
is a passthrough the rules can't check, and a secret inside it can't get a `Has*` flag or its own
encryption (ADR 0007). And an adapter that claims a kind it can't serve yet — a stub that throws
`NotImplementedException` — fails only when someone uses it. Vasyl decided that MAM-7's adapters
implement their registration and a real connection test, and nothing of any kind contract.

## Decision

**An integration stores the id of an adapter type that Infrastructure registers (`jellyfin`,
`torznab`, `qbittorrent`); the core reaches a service only through `IIntegrationAdapter` and the kind
contracts in Core, found by that id, and no Core namespace, type or member is named after a
product.**

**A type id is part of the on-disk contract.** It matches `^[a-z][a-z0-9-]{1,31}$`, is compared byte
for byte and is stored on every integration of the type. Renaming one orphans those rows: they stay
listed with `adapterDisplayName: null`, editing or testing them answers 409 ("This version of the app
can't connect to …, so this integration can only be removed."), and removing them works.

**The registry is a Strategy.** `IntegrationAdapterRegistry` indexes every registered
`IIntegrationAdapter` by its descriptor's type id and throws, naming the offender, when it is built
with two adapters of one id, a malformed id or a type without a display name. A kind contract
(`IMediaLibraryProvider`, `IReleaseSearchProvider`, `IDownloadClient`, `IMetadataResolver`) is a
further interface of the same adapter class; `Find<T>(typeId)` returns the adapter only when it
implements `T`, so a type can't be used for a kind it doesn't serve, and there is no second
registration to forget. `IFileInventory` is not one: it is the app's own look at its local mounts,
not a connection, so it isn't found by a type id and answers its own `FileInventoryStatus`. The
descriptor says what the type takes: a kind, three credential slots (user name, password, API key),
each `NotUsed`, `Optional` or `Required`, and whether the service reports file paths, which decides
whether path mappings apply (ADR 0003).

**One outcome for every call.** A call to an integration ends in one `IntegrationCallStatus`
(`Succeeded`, `Unreachable`, `TimedOut`, `AuthenticationFailed`, `Refused`, `ServiceError`,
`UnexpectedResponse`, `NotFound`) and one readable sentence, and never throws on an expected
failure; only the caller's own cancellation propagates. The page colours a result by its status,
never by a product's field.

**A connection test answers 200 whatever the service said.** The request asks "does it connect?",
and "no, because the key was refused" is its answer, not a failure of this API: 200
`{ status, message }` for every outcome. 409 stays for what the app itself can't do: a stored secret
it can't read, or a type this build no longer registers.

**Only the test, for now.** Each later ticket adds its kind to the same class — MAM-8 the library to
`JellyfinAdapter`, MAM-10 the search to `TorznabAdapter`, MAM-11 to MAM-14 the client to
`QBittorrentAdapter` — with a convention test that every adapter whose descriptor names a kind
implements that kind's contract. That test can't exist in MAM-7, where the decision makes it fail.

| Rejected option | Why not |
| --- | --- |
| A product enum in Core (`IntegrationType.Jellyfin`) | Product names in the core, and a Core change and a migration for every adapter |
| A table or entity per product | A migration and a settings screen per product; lists and provenance would join across them |
| A JSON settings bag per row | A passthrough the rules can't check; a secret inside gets no `Has*` flag and no encryption of its own |
| Stub kind implementations | Claims a capability that fails at run time; the decision was a real test and nothing more |
| A second registration per kind contract | One more line to forget per adapter; `Find<T>` asks the adapter that is already there |
| 502 for a failed connection test | Reports the service's "no" as the app's failure; the dialog needs the sentence either way |

`CoreIsVendorNeutralTests.No_core_type_member_or_namespace_is_named_after_a_product` refuses a
product name in Core (doc comments may name products).
`IntegrationsInfrastructureExtensionsTests.The_registry_lists_jellyfin_torznab_and_qbittorrent_once_each_with_their_descriptors`
pins the three types and what each takes, and
`IntegrationsConnectionTestHttpTests.Test_is_200_with_the_adapters_status_and_message` the 200 for
every status. `IntegrationsUpdateHttpTests.Update_of_an_integration_of_an_unregistered_type_is_409_with_the_remove_only_sentence`
and `IntegrationsDeleteHttpTests.Delete_of_an_integration_of_an_unregistered_type_is_204` pin the
orphaned row.

## Consequences

A Phase D Transmission or SABnzbd adapter is one class that implements `IIntegrationAdapter` and its
kind contract, one registration and one named HTTP client; the settings page, the rules, the
encryption and the registry need nothing. The three credential slots cover every product the
specification and the roadmap name (a key for Jellyfin, Emby, Plex, Prowlarr, Jackett and SABnzbd; a
user name and password for qBittorrent, Transmission and Deluge); a product that needs a fourth kind
of credential would need a new column.

The kind contracts carry only their Phase A members. MAM-8 to MAM-14 add members as they implement
them, and amend this record when a decision changes.

## Anti-consequence

This keeps product names out of the core's code, not out of its knowledge: doc comments name the
products a contract was shaped against, and the Torznab adapter speaks Torznab, not Prowlarr. It
doesn't make a type id free to rename.

## Implementation (added 0.1.0, MAM-8)

**Jellyfin serves its kind.** `JellyfinAdapter` implements `IMediaLibraryProvider` (one item can
hold several versions), so `Find<IMediaLibraryProvider>("jellyfin")` returns it while `torznab` and
`qbittorrent` still return nothing. The library sync asks the registry by the stored type, and a
media library integration whose type can't read a library is refused with 409 ("This version of the
app can't read a library from “…”."), never by a product name. Kind calls get the test's
guarantees: `HttpIntegrationAdapter.CallAsync<T>` (the Template Method, extended) maps transport
failures and timeouts to the same statuses and sentences and masks the call's secrets, from one
source of sentences shared with the test.

**The version gate is the connection test; the contract has no member for it.** An adapter refuses
a server version it can't read in `TestConnectionAsync`, with the new status `UnsupportedVersion`
(the list above gains it), and a sync runs that test before asking for anything else: the owner's
**Test** button and a sync answer with the same sentence, and the test still answers 200. Jellyfin's
minimum is 10.9, Vasyl's choice. Only 12.1.0 was sampled live; 10.9 to 10.11 are accepted from
reading Jellyfin's source (a user-less `/Items` with an API key, and the `MediaBrowser Token`
header, work since 10.9). An older version gets "This is Jellyfin 10.8.13; the app reads libraries
from Jellyfin 10.9 or later. Update Jellyfin, then test again.", and one without a leading
`major.minor` the sentence that it can't be compared with 10.9: refused, not read through answers
nobody has seen.

**The convention test exists, for the kinds implemented so far.**
`AdapterKindContractTests.Every_media_library_adapter_implements_the_media_library_contract`
(Infrastructure.Tests) takes the registry the app builds and checks that every type whose
descriptor names `MediaLibrary` implements `IMediaLibraryProvider`, and that it found at least one.
Release search joins it with MAM-10 and download clients with MAM-11, when their contracts are
implemented; their adapters still only test a connection.

`JellyfinAdapterTests.TestConnection_of_jellyfin_before_10_9_is_an_unsupported_version`,
`JellyfinAdapterTests.TestConnection_of_a_version_it_can_not_compare_is_an_unsupported_version` and
`LibrarySyncFailureHttpTests.Sync_of_a_jellyfin_older_than_10_9_fails_before_asking_for_anything_else`
pin the gate, `IntegrationsConnectionTestHttpTests.Test_is_200_with_the_adapters_status_and_message`
its 200, and `IntegrationsInfrastructureExtensionsTests.The_registry_finds_jellyfin_and_nothing_else_as_a_media_library`
and `LibrarySyncStartHttpTests.Start_of_a_media_library_whose_type_cant_read_a_library_is_409_with_its_sentence`
the registry's answer.

## Implementation (added 0.1.0, MAM-9)

**The file inventory has its implementation, outside the registry as decided.** `LocalFileInventory`
(Infrastructure) is registered as `IFileInventory` itself, not found by a type id, and answers
`FileInventoryStatus`, which gained `MediaUnreadable`, `InspectorUnavailable` and
`ChangedWhileReading`. The contract gained the media read (`InspectMediaAsync`, returning
`MediaFacts`: duration, bit rate and tracks), and both members now take the allowed roots of the call,
read by the caller once per inspection from the owner's setting instead of a value cached in the
inventory
([ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)).
ffprobe is named in Infrastructure only (the process seam, its arguments and the parser of its JSON);
the core speaks of a media inspector and of `DynamicRange`, and `CoreIsVendorNeutralTests` now
refuses `ffprobe`, `ffmpeg` and `mediainfo` in a name in Core, so it stays that way. The API tests replace `IFileInventory` with `FakeFileInventory`, the way they replace the
vendors' networks; the local implementation is tested against real folders and a stub ffprobe in
Infrastructure.Tests.

## Implementation (added 0.1.0, MAM-10)

**Torznab serves its kind.** `TorznabAdapter` implements `IReleaseSearchProvider` — capabilities,
search and the payload fetch — so `Find<IReleaseSearchProvider>("torznab")` returns it while `jellyfin`
and `qbittorrent` still return nothing, and the convention test covers release search too.

**The core decides the query; the adapter translates it.** The search contract's Phase A members
changed as the Consequences above allowed. `ReleaseSearchQuery` now carries its mode (the source's
movie search or its general search, `ReleaseSearchMode`), its limit and only what the source takes:
`ReleaseSearchPlanning`, in Core, chooses it from `ReleaseSearchCapabilities`, which gained whether the
source has a text search, whether its movie search takes text, and its result limit. A category says
whether it holds movies, since only the adapter knows its protocol's numbering. A hit gained its peers,
and its id may be missing. Which id or year a source can take is then one tested rule, not something
each adapter decides its own way.

**A search answers 200 whatever its sources said.** As a connection test does, but per source: each
enabled release source answers with its status and, for the owner, its sentence, beside its releases,
and a failing one never hides the others (`POST /api/releases/search`); 400 is only for a search the
app doesn't take. What a connection test or a sync answers with 409 — a stored key that can't be read,
a type this build no longer registers — and a source that offers no search the app can use become that
source's `notSearchedReason` (`SecretUnreadable`, `TypeUnavailable`, `NoUsableSearch`) with a `null`
status: the app didn't ask it. The sources are asked at once, and an exception an adapter should never
have thrown costs its source the results (`UnexpectedResponse`), not the whole search.

`AdapterKindContractTests.Every_release_search_adapter_implements_the_release_search_contract` and
`IntegrationsInfrastructureExtensionsTests.The_registry_finds_torznab_and_nothing_else_as_a_release_search_source`
pin the kind, `ReleaseSearchPlanningTests` the planning, and
`ReleaseSearchSourcesHttpTests.Search_with_one_failing_source_is_200_with_the_other_sources_releases`,
`ReleaseSearchSourcesHttpTests.Search_of_a_source_whose_key_can_not_be_read_says_so_without_asking_it`
and `ReleaseSearchSourcesHttpTests.Search_of_a_source_whose_answer_the_app_can_not_read_keeps_the_others`
the answer per source.

## Implementation (added 0.1.0, MAM-11)

**qBittorrent serves its kind.** `QBittorrentAdapter` implements `IDownloadClient`, so
`Find<IDownloadClient>("qbittorrent")` returns it while `jellyfin` and `torznab` return nothing, and
the convention test covers download clients too: every kind an adapter is registered for now has its
contract implemented. The destinations ask the registry by the stored type, and a download client
whose type this build can't send downloads to is refused by type ("This version of the app can't
send downloads to “…”."), never by a product name.

**The contract carries only what is implemented (Vasyl's choice).** The Decision rejected stub kind
implementations, and the Consequences let a ticket change its contract's Phase A members. So
`IDownloadClient` lost `SubmitAsync`, `ListJobsAsync` and `ListJobFilesAsync`, which MAM-13 and
MAM-14 add back with their implementations, and keeps `Capabilities` and `ListDestinationsAsync`; the
records the removed members use (`DownloadSubmission`, `SubmissionReceipt`, `ClientJob`,
`ClientJobFile`, `ClientJobState`) stay in Core for them. qBittorrent's capabilities take a .torrent
file and a magnet link, give each category a folder, and promise no job id from a submission:
`torrents/add` answers only `Ok.` before 5.2, so with 4.4.1 the oldest version taken, the info hash
will correlate instead (MAM-13).

**The version gate covers the listing too.** As for Jellyfin, the connection test refuses an old
server with `UnsupportedVersion` and still answers 200: "This is qBittorrent v4.3.9; the app works
with qBittorrent 4.4.1 or later. Update qBittorrent, then test again." The minimum is Vasyl's choice:
4.4.0 lists a category's folder as `save_path` instead of `savePath` (qBittorrent issue #15969), and
older versions differ more. The destination listing reads `app/version` anyway, since the folder rules
depend on it, and refuses the same versions before it reads a category, so a save on an old
qBittorrent answers 502 with that sentence. Only 5.2 was verified live (5.2.3 in the samples, 5.2.4
through the app on 2026-10-01); 4.4.1 to 5.1 are accepted from reading qBittorrent's source. A version it can't compare is refused, never guessed.

**A client that can't be asked is part of the answer.** As a connection test and a search do, the
destination list and a client's category list answer 200 whatever the clients said: a switched-off
client isn't asked (`ClientDisabled`), and one that couldn't be — its call failed, its type isn't
registered, its stored secret can't be read — gives each of its destinations `ClientUnavailable` and
the sentence. Each client on a page is asked once, all of them at once, and an exception an adapter
should never have thrown costs its client the answer, logged with its type and stack but never its
message. Only a save, which needs the client to confirm the category (ADR 0010), turns the failure
into 409 (not asked) or 502 (asked, and it couldn't answer).

`AdapterKindContractTests.Every_download_client_adapter_implements_the_download_client_contract` and
`IntegrationsInfrastructureExtensionsTests.The_registry_finds_qbittorrent_and_nothing_else_as_a_download_client`
pin the kind, `QBittorrentAdapterTests.TestConnection_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version`,
`QBittorrentAdapterTests.TestConnection_of_a_version_it_can_not_compare_is_refused_rather_than_guessed`
and `QBittorrentAdapterDestinationsTests.ListDestinations_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version_and_reads_no_categories`
the gate, `QBittorrentAdapterDestinationsTests.Capabilities_take_torrent_files_and_magnets_into_categories_with_folders_and_promise_no_job_id`
the capabilities, and `DestinationsListHttpTests.List_of_a_client_that_could_not_be_asked_is_200_with_client_unavailable_and_the_reason`,
`DestinationsListHttpTests.List_of_a_client_whose_answer_the_app_can_not_read_keeps_the_others_and_logs_no_message`
and `DestinationsCreateHttpTests.Create_when_the_client_could_not_answer_is_502_with_its_sentence_and_writes_nothing`
the answer per client.

## Implementation (added 0.1.0, MAM-13)

**The submission is back, and the client's jobs are looked up by info hash.** `IDownloadClient`
gains `SubmitAsync`, sent exactly once — never again by the call or a retry of it, since a write sent
twice may act twice — and `FindJobsAsync(settings, infoHashes)`: the client's jobs that hold any of
the hashes (40 hexadecimal characters for v1, 64 for v2), each with every hash the client lists for
it (`ClientJob.InfoHashes`, new), for the core to match (`ClientJobCorrelation`), never by name. It
takes the place of MAM-7's `ListJobsAsync`, which the MAM-11 section said MAM-13 and MAM-14 would
add back: a submission is confirmed by the torrents that hold its hashes, an "every job" listing
would read a whole client to find one torrent, and following a job by the id stored on it (MAM-14)
asks the same question. `ListJobFilesAsync` stays MAM-14's, with `ClientJobFile`.
`DownloadSubmission.ExpectedInfoHash` is now the hash the app worked out from the payload, never the
listing's claim
([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).

**"No job id promised" holds, and the info hash correlates as the MAM-11 section said.** 4.4.1 to
5.1 answer `torrents/add` with `Ok.` only; 5.2 answers a JSON object whose `added_torrent_ids` the
adapter passes on only when it lists exactly one. Any other answer leaves the receipt without an id,
and the job is found by its hashes. A download client of a type this build can't send to is refused
by type ("This version of the app can't send downloads to “…”."), as the destinations refuse it.

`DownloadsSendHttpTests.Create_against_qbittorrent_before_5_2_finds_the_job_by_its_info_hash` pins
the correlation over HTTP, `ClientJobCorrelationTests` the matching, and
`QBittorrentAdapterSubmitTests` the answers of each version.

## Implementation (added 0.1.0, MAM-14)

**The files come back, and the client's jobs are read by destination too.** `IDownloadClient` gains
`ListJobFilesAsync(settings, clientJobId)`, MAM-7's member with its signature, which the MAM-11
section said MAM-14 would add back: the job's files in the client's order, each a `ClientJobFile`
whose path is now `RelativePath` — relative to the job's folder in the client, exactly as the client
names it — rather than MAM-7's `ServicePath`, which nothing used; an empty list for a job whose files
the client doesn't know yet (a magnet's metadata), and `NotFound` for a job the client no longer has.
And `ListDestinationJobsAsync(settings, destinationKey)`: every job of one destination, read as
`FindJobsAsync` reads them, for the job a client renamed after it was sent, which its stored hashes no
longer find ([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)'s
gap) — never to read a whole client. qBittorrent serves them with `GET api/v2/torrents/files?hash=`
and `GET api/v2/torrents/info?category=`, after the same session and version gate as every read.

**Following a job is a read, and the contract has no member that removes.** No member removes,
deletes, stops or pauses a job or its files, so cancelling a job in the app can't reach the client
([ADR 0014](0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md)).
The one write is still `SubmitAsync`. `DownloadClientNeverRemovesTests` keeps it so, by the verbs in
the members' names, and checks that it scanned every member of the contract. The core decides what a
client's job means for the app's (`DownloadJobFollowing`), so a second download client maps its states
onto `ClientJobState` and nothing more.

`QBittorrentAdapterFilesTests.ListJobFiles_reads_the_three_files_of_the_sample_exactly`,
`QBittorrentAdapterFilesTests.ListJobFiles_of_a_torrent_removed_in_qbittorrent_is_not_found`,
`QBittorrentAdapterDestinationJobsTests.ListDestinationJobs_finds_a_hybrid_qbittorrent_renamed_with_the_v1_hash_among_its_hashes`
and `DownloadClientNeverRemovesTests.No_download_client_member_is_named_for_removing_deleting_stopping_or_pausing`
pin it.

## Implementation (added 0.2.0, MAM-32)

**The contracts learn TV, and the core still decides.** As the Consequences allowed, MAM-32 adds
members to two kind contracts and no product name to the core. The media library contract reads one
list at a time: `LibraryPageRequest` gains the list (`Kind`: the library's movies, series or
episodes, each counted on its own), and a `LibraryItem` gains where an episode sits
(`LibraryEpisode`: its series item and its numbers) and whether the library holds no file for it
(`IsMissing`); the item's kind is the library's word alone
([ADR 0019](0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).
The release search contract gains a TV search in the capabilities (`ReleaseTvSearchCapabilities`:
whether it takes a title, which ids, whether a season), a TV mode and a season in the query
(`ReleaseSearchMode.Tv`, `ReleaseSearchQuery.Season`), and what a hit is (`ReleaseSearchHit.Kind`).
`ReleaseSearchPlanning.ForTv`, in Core, chooses the TV queries as `For` chooses the movie ones;
`JellyfinAdapter` translates a list into `includeItemTypes`, and `TorznabAdapter` a TV query into
`t=tvsearch` with `season=`, `tvdbid=` or a show's `tmdbid=`.

**The adapter knows its numbering, so it says what a category and a hit are.** A category says
whether it holds TV (Torznab's 5000 to 5999) and whether it is the source's own section rather than
a standard category (`IsSourceSpecific`, Torznab's ids of 100000 and up), as MAM-10's category says
whether it holds movies; a hit is TV when it lists a TV category, and a TV listing's TMDb id is a
show's (`TmdbTv`)
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
The connection test names the TV search beside the movie search ("…; movie search by title, TV
search by title, season and episode.").

**A source's sections are read as its search is.** The Destinations dialog's tracker sections come
from `GetCapabilitiesAsync`, every source asked at once each time (`IReleaseSourceCategoryReader`),
and the list answers 200 whatever the sources said: a switched-off source, one of a type this build
no longer registers and one whose key can't be read are not asked and say why, and one whose call
failed gives its sentence, masked of its secrets, beside its mapped sections; an exception an
adapter should never have thrown costs its source the answer, logged with its type and stack but
never its message.

`JellyfinAdapterTvTests.ListItems_asks_for_one_page_of_the_list_with_its_fields`,
`JellyfinAdapterTvTests.ListItems_fails_the_page_on_an_item_of_another_type_than_the_list`,
`TorznabAdapterSearchIdentifiersTests.Search_tells_a_tv_listing_from_a_movie_by_its_categories`,
`TorznabAdapterSearchIdentifiersTests.Search_reads_a_tmdb_id_in_the_space_of_the_listings_kind`,
`ReleaseSearchPlanningTests.ForTv_never_sends_a_movies_tmdb_id_to_the_tv_search` and
`DestinationsSectionsHttpTests.Sections_of_a_source_that_can_not_be_reached_answers_its_sentence_and_its_mapped_sections_beside_the_others`
pin it.

## Implementation (added 0.2.0, MAM-18)

**The file inventory gains the walk of the allowed roots, still outside the registry.**
`IFileInventory.ListMediaFilesAsync(allowedRoots, cancellationToken)` yields the media file names it
finds under the roots the caller read, in the owner's order, as an `IAsyncEnumerable` of
`FileListingEntry` — a status, the name's or the place's app path, for a media file where it really
is, and for anything else a sentence — so the scan reads a walk as it goes, its progress moving with
it, and can stop it between two entries. `FileListingStatus` says what an entry is: `MediaFile`,
`Unlisted` (a place the walk couldn't list now, whose earlier names stand), `Skipped` (a place it
refuses) and `LimitReached`, given last. Which names are media files is the core's rule (`MediaFileNames`), so a
second inventory takes the same ones
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).
`LocalFileInventory` walks with `FolderWalker`, and its platform reader, the Strategy that already
opens a file, gains `OpenFolder`: on Linux the folder opened name by name without following a link
and listed through its descriptor, with its device and inode; on Windows and elsewhere the folder's
own path and no identity. `FakeFileInventory` lists what a test plants, a linked name and a listing
problem included, the way the API tests already replace the inventory.

`LocalFileInventoryListingTests.List_walks_the_roots_in_the_owners_order`,
`LinuxFileStatusReaderTests.OpenFolder_lists_the_folder_it_opened_even_after_its_path_leads_elsewhere`
and `FolderScanRunHttpTests.Scan_gives_the_inventory_the_allowed_roots_in_the_owners_order` pin it.

## Implementation (added 0.2.0, MAM-19)

**The file inventory gains the hash, still outside the registry.**
`IFileInventory.HashAsync(appPath, allowedRoots, cancellationToken)` reads every byte of a file
inside the roots the caller read and answers a `FileContentHash` — the file's facts, the same before
and after the read, and the SHA-256 in lowercase hex — or `ChangedWhileReading` when the file changed
meanwhile; its other refusals are a look's, and `FileInventoryStatus` gains no member
([ADR 0022](0022-a-file-is-hashed-only-when-a-file-of-another-identity-shares-its-size-and-could-be-its-copy-as-the-last-step-of-the-inspection-one-at-a-time.md)).
Which files need a hash is the inspection's to decide, not the inventory's. `LocalFileInventory`
reads through its platform reader's opened file, which gains `ReadAsync`: on Linux and Windows a
read at an offset of the handle opened after the checks; elsewhere the path, opened on the first
read, where no identity is read and so nothing is hashed. `FakeFileInventory` answers hashes too —
by default a digest of the file's identity, size and time, so two names of one file hash alike — and
records each path it hashed, the roots it was given and how many hashes ran at once.

`LocalFileInventoryHashTests.Hash_reads_a_large_file_in_chunks_of_one_mebibyte`,
`LocalFileInventoryHashTests.Hash_of_two_copies_is_one_digest_with_two_identities` and
`FileInspectionHashHttpTests.Inspect_gives_the_hash_the_allowed_roots_in_the_owners_order` pin it.

## A metadata catalog (added 0.2.1, MAM-33)

**A fourth kind, and its adapter.** `IntegrationKind` gains `Metadata` (4), "Metadata catalog" on the
Integrations page, and the registry a fourth type, `tmdb` ("TMDb", `TmdbAdapter`): it takes an API
key only — TMDb's v4 "API Read Access Token", required — and reports no file paths, so it takes no
path mappings (Vasyl's decision 1). The Decision's list of type ids now reads `jellyfin`, `torznab`,
`qbittorrent` and `tmdb`, and the test it names,
`IntegrationsInfrastructureExtensionsTests.The_registry_lists_jellyfin_torznab_and_qbittorrent_once_each_with_their_descriptors`,
is now `IntegrationsInfrastructureExtensionsTests.The_registry_lists_jellyfin_torznab_qbittorrent_and_tmdb_once_each_with_their_descriptors`.

**`IMetadataResolver` has its first implementation, and `IMetadataCatalog` stands beside it.** MAM-12
left the resolver unimplemented, as a search is matched against the library snapshot and needs no
call to the media server; it is implemented now by the catalog's adapter, not the media server's,
and used only for recommendations
([ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md)).
`LookupAsync` answers every candidate with its evidence and picks none: by the work's own TMDb or TMDb
TV id (its details), else by an IMDb id (TMDb's find), else by a title and year (its search, kept to
the results whose title key is the query's). `IMetadataCatalog` is a new kind contract of the same
class, as every kind contract is: a work's details with its credits and recommendations in one call,
a collection, a person's works, a search, a find by IMDb id, discovery by genre, the genre lists and
a poster. A type serves the Metadata kind only by implementing both, and
`AdapterKindContractTests.Every_metadata_adapter_implements_the_resolver_and_the_catalog_contracts`
checks it.

**Which catalog a computation uses is the connector's choice.** `CatalogConnector` takes the first
switched-on integration of kind `Metadata`, by its normalised name, whose type this build registers
and serves both contracts; a type it no longer registers is passed over as if switched off. With
none, a computation is refused with "Connect a metadata catalog under Integrations first." (409 for
the owner's **Recompute now**). Its settings come from `IIntegrationSettingsFactory`, and a stored
token that can't be read fails the run with that factory's sentence, as a sync's does.

**What TMDb is spoken to with.** The owner enters `https://api.themoviedb.org`; the adapter appends
`/3` unless the address already ends in it. The token goes only in `Authorization: Bearer`, never as
v3's `api_key=` in a query
([ADR 0007](0007-an-integration-credential-is-sealed-to-its-row-and-never-leaves-through-the-api.md)).
Every call is a GET in Ukrainian (`language=uk-UA`), the original title read beside the translated
one, and every answer is read field by field by name. The connection test is
`GET /3/authentication`: "Connected to TMDb.", or for a token TMDb refuses "TMDb refused the key (401
Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API, not the shorter
API key."; a run passes the same test before it asks anything else, the gate MAM-8 set for a sync.
`NotFound` comes only from TMDb's own 404, whose JSON says `status_code` 34; a bare 404 means the
address isn't TMDb's API ("TMDb's API wasn't found at this address (404 Not Found). Use
https://api.themoviedb.org."). Discovery asks only for works at least 300 people rated.

**Paced, and patient with a 429.** TMDb limits requests per address, "somewhere in the 50 requests
per second range" by its documentation, so the one adapter instance holds one gate for every caller
(`TmdbRequestPacer`): two requests never start less than 25 ms apart, at most 40 a second. A 429 holds
back every caller for its `Retry-After` — 2 seconds without one, 10 at most — and the same GET is
sent again, at most three times; then the call is `Refused` with "TMDb is asking the app to slow down
(429 Too Many Requests), and still was after three retries. Try again in a minute." Only reads are
ever sent again; the adapter writes nothing.

**The release search contract learns the newest releases and a description.** `ReleaseSearchMode`
gains `Latest` — the source's general search with no text, in the asked categories — which
`ReleaseSearchPlanning.ForLatest` plans, and `ReleaseSearchHit` gains `Description`, the listing's own
description when it says more than the title. The MAM-32 section's "a hit is TV when it lists a TV
category" now reads: a hit is TV when it lists a TV category other than TV/Anime (5070) and
TV/Documentary (5080), and in those two only when its title or description names seasons or
episodes; a movie category says it holds movies for 5070 and 5080 too, so the movie search asks them
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).

`TmdbAdapterTests.Descriptor_is_a_metadata_type_that_takes_an_api_key_only_and_reports_no_paths`,
`TmdbAdapterTests.TestConnection_reads_the_authentication_sample_with_the_token_in_the_bearer_header`,
`TmdbAdapterTests.TestConnection_with_a_wrong_token_is_authentication_failed_from_the_recorded_401`,
`IntegrationsInfrastructureExtensionsTests.The_registry_finds_tmdb_and_nothing_else_as_a_metadata_resolver_and_catalog`,
`TmdbAdapterLookupTests.Lookup_by_a_title_and_year_keeps_the_titles_that_match_with_their_evidence`,
`TmdbAdapterWorksTests.GetWork_of_a_movie_asks_for_its_details_credits_and_recommendations_in_ukrainian`,
`TmdbAdapterWorksTests.GetWork_answered_404_without_tmdbs_body_is_an_unexpected_response_not_a_missing_work`,
`TmdbAdapterRateLimitTests.Requests_of_callers_at_once_start_at_least_25_ms_apart`,
`TmdbAdapterRateLimitTests.A_429_four_times_is_refused_after_three_retries` and
`RecommendationRunWorkerHttpTests.RunQueued_with_the_key_refused_fails_with_the_catalogs_sentence_after_the_connection_test_alone`
pin it.

## A library that sets an item's ids (added 0.2.1, MAM-34)

**An optional capability, found like a kind contract.** A media library may implement
`IMediaLibraryIdentifierWriter` (`Core/Abstractions/Library`), a further interface of the same
adapter class. `WriteIdentifiersAsync` takes a `LibraryIdentifierChange` — an item id, its kind and
edits of its registry ids, each a scheme, a value (`null` removes the id) and a rule, `Always`,
`OnlyWhereAbsent` (only where the item holds no value of the scheme) or `OnlyWhereExpected` (only
where it still holds the value the app wrote) — and answers a `LibraryIdentifierWrite`: the ids before
and after, the edits applied, whether anything was sent, whether the read afterwards confirmed it, and
why not. `WritableKinds` names the kinds of item the library is written on — a film (`Movie`), a show
(`Series`) at most — and `UnwritableKindReason` gives the library's own sentence for any other, so
the caller keeps such a link in the app because the library says so, never because of which product
it is (Vasyl's decision of 2026-10-06: Jellyfin writes films only). `Find<IMediaLibraryIdentifierWriter>(typeId)`
finds it as the Decision finds every kind contract; a type without it keeps the owner's links in the
app ([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
It is no kind contract: `AdapterKindContractTests.Every_media_library_adapter_implements_the_media_library_contract`
still asks only for `IMediaLibraryProvider`. `JellyfinAdapter` implements it; the word
`ProviderIds` stays in Infrastructure, and no Core name names the product.

**The library contract's one write, sent once and read again.** Like qBittorrent's `torrents/add`,
the change is sent once and never again; only reads are. The Jellyfin adapter reads `GET /Users`
for the first enabled administrator, reads the item as that user (`GET /Items/{id}?userId=`; with the
key alone Jellyfin answers 400), posts it back as read with only `ProviderIds` changed — a key in the
spelling the item already uses, every other id kept — and without its `People`, which Jellyfin would
rebuild from their names, roles and types alone, and reads it again; nothing is posted when no edit
changes anything. A movie item only: every save of a series rewrites and saves all its seasons and
episodes, as Jellyfin's source shows, so `WritableKinds` is `[Movie]`, the sentence for a series
"Jellyfin rewrites every season and episode of a series whenever it saves the series, so a series
link stays in the app.", and a series change a caller's mistake. The Decision's one outcome holds: a 404 is `NotFound` "Jellyfin
no longer has this item; sync the library and link it again.", a 401 the key-refused sentence, a 403
`Refused` "Jellyfin refused to change the item's ids. Check that the API key belongs to an
administrator.", a 400 to the change `Refused` "Jellyfin refused the item it was sent back; nothing
was changed.", no administrator `Refused` "Jellyfin has no enabled administrator account to read the
item as.", and an item of another type `UnexpectedResponse`, nothing written. One refusal comes from
what Jellyfin's source says, not from the live check: a 404 to the administrator's read of an item the
user-less `GET /Items?ids=` still lists is `Refused` — the item is hidden from that user, not gone.
Once the change has left — a 2xx, a 5xx, an answer lost or late — the call succeeds as sent, confirmed only when the read
again shows every applied edit, and otherwise with the sentence why; a caller's mistake throws before
anything is sent. The version gate (MAM-8) covers the write: the caller passes the connection test
first. `LibraryIdentifierWriterWritesIdsOnlyTests` keeps the contract free of any member that
refreshes, identifies, sets images, titles or locks, or deletes, as `DownloadClientNeverRemovesTests`
keeps the download client's. The MAM-33 section's "the adapter writes nothing" holds for TMDb's.

**The catalog says a little more.** `CatalogWork` gains a runtime in minutes (a film's `runtime`, a
show's first `episode_run_time`, else its latest episode's), an overview and the other titles TMDb
knows it by, and a show's IMDb id is read too: a work's details now ask
`append_to_response=credits,recommendations,alternative_titles,external_ids`. Nothing else in the
catalog contract changes.

`IntegrationAdapterRegistryTests.Find_of_the_optional_identifier_writer_returns_only_a_library_that_implements_it`,
`AdapterKindContractTests.Jellyfin_writes_identifiers_and_every_identifier_writer_is_a_media_library`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_reads_the_users_then_the_item_as_the_administrator_posts_it_once_and_reads_it_again`,
`AdapterKindContractTests.Every_identifier_writer_writes_only_films_or_shows_and_says_why_it_keeps_the_rest_in_the_app`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_posts_the_movie_as_read_without_its_people_and_with_only_its_provider_ids_changed`,
`JellyfinAdapterIdentifiersTests.WritableKinds_are_films_only_and_every_other_kind_says_why_its_link_stays_in_the_app`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_answers_the_refusals_jellyfin_recorded_with_their_status_and_sentence`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_whose_answer_is_lost_is_sent_and_not_confirmed_and_never_sent_again`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_refuses_a_callers_mistake_without_sending_anything`,
`LibraryIdentifierWriterWritesIdsOnlyTests.No_identifier_writer_member_is_named_for_refreshing_identifying_images_titles_locks_or_deleting`
and `TmdbAdapterWorksTests.GetWork_of_a_show_reads_the_planet_earth_sample_with_its_runtime_imdb_id_and_each_alternative_title_once`
pin it.

## A library looks an item up after its ids are written (added 0.2.1, MAM-34)

**One more member of the optional capability (Vasyl's decision of 2026-10-06).**
`IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync(settings, sourceItemId, kind)` asks the
library to look up an item's missing metadata with its own providers, by the ids it now holds, and
answers a `LibraryMetadataLookup` — the library took the request; what the lookup finds is the
library's to say. It is sent once and never again; a kind `WritableKinds` leaves out, or an item id the
library never gives, is a caller's mistake that throws before anything is sent; `NotFound` is the item
gone. The Jellyfin adapter sends one `POST /Items/{id}/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false`,
exactly Jellyfin's own "Search for missing metadata", which fills what is missing and replaces nothing;
204 is queued, 401 the key-refused sentence, 404 "Jellyfin no longer has this item; sync the library
and link it again.", 403 `Refused` "Jellyfin refused to look the item up. Check that the API key belongs
to an administrator.", 400 `Refused` "Jellyfin refused the request to look the item up; nothing was
changed.", anything else the shared sentence. `LibraryIdentifierWriterWritesIdsOnlyTests` now allows
exactly this one name beyond ids — the scan reads the members' parameters too, so no flag that could
make it replace anything can join it — and still refuses any identify, image, delete or other refresh
member ([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).

**Jellyfin writes series too (Vasyl's final decision of 2026-10-06).** `WritableKinds` is
`[Movie, Series]`: a series item takes the show's TMDb id (`TmdbTv`) under Jellyfin's `Tmdb` key and
the IMDb id, a movie item the film's (`Tmdb`) and the IMDb id, and a change sending a film's id to a
series item or a show's to a movie item is a caller's mistake. The lookup is asked for a series as for
a film. Held back at first that day — the recorded save of the Penguins of Madagascar gave all 3
seasons and 149 episodes the series' `TV-Y7`, which the lookup didn't change back —, it was decided on
those facts: age ratings don't matter to Vasyl (ADR 0026). An episode item stays a caller's mistake,
its sentence now "Jellyfin's ids are written on movie and series items only, so a link on an episode
stays in the app."; the sentence for a series ("Jellyfin rewrites every season and episode of a series
whenever it saves the series, so a series link stays in the app.") is gone.

**The catalog names an episode's own IMDb id.** `IMetadataCatalog.GetEpisodeAsync(settings, showId,
seasonNumber, episodeNumber)` answers a `CatalogEpisode` — its numbers, title, air date, runtime and IMDb
id — or `NotFound` for an episode the source doesn't know; a malformed show id or a negative number
throws before anything is sent. TMDb's adapter asks `GET /3/tv/{id}/season/{s}/episode/{e}?append_to_response=external_ids&language=uk-UA`
and reads the IMDb id from `external_ids.imdb_id`, as a show's.

`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_posts_search_for_missing_metadata_once_with_no_body_and_jellyfin_queues_it`,
`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_of_a_series_posts_the_same_search_once_and_jellyfin_queues_it`,
`JellyfinAdapterMetadataLookupTests.RequestMetadataLookup_refuses_a_callers_mistake_without_sending_anything`,
`JellyfinAdapterIdentifiersTests.WritableKinds_are_films_and_series_and_an_episode_item_says_why_its_link_stays_in_the_app`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_of_a_series_posts_it_once_as_read_without_its_people_with_the_shows_id_under_tmdb_and_reads_it_again`,
`JellyfinAdapterIdentifiersTests.WriteIdentifiers_refuses_a_callers_mistake_without_sending_anything`,
`AdapterKindContractTests.Every_identifier_writer_writes_only_films_or_shows_and_says_why_it_keeps_the_rest_in_the_app`,
`LibraryIdentifierWriterWritesIdsOnlyTests.The_one_name_beyond_ids_is_the_missing_metadata_lookup_and_it_takes_nothing_that_could_replace`
and `TmdbAdapterEpisodesTests.GetEpisode_with_a_callers_mistake_throws_without_sending_anything` pin it.
`JellyfinAdapterIdentifiersTests.WritableKinds_are_films_only_and_every_other_kind_says_why_its_link_stays_in_the_app`,
named in the section above, is now
`JellyfinAdapterIdentifiersTests.WritableKinds_are_films_and_series_and_an_episode_item_says_why_its_link_stays_in_the_app`.

**Amended 2026-10-06 (ADR 0026's Identify section, added in 0.2.1).** The two sections above — "A
library that sets an item's ids" and "A library looks an item up after its ids are written" — are
replaced as they stand, and stay as history. The optional capability is now
`IMediaLibraryItemIdentifier` (`Core/Abstractions/Library`): `IdentifiableKinds` (Jellyfin: film and
series, never an episode item), `UnidentifiableKindReason` and `IdentifyAsync`, taking a
`LibraryIdentifyRequest` — the item, its kind, the one identifier and whether to protect held ids —
and answering a `LibraryIdentification`. It is found exactly as before, by
`Find<IMediaLibraryItemIdentifier>(typeId)`, and is still no kind contract; the six Core types of the
two sections above (`IMediaLibraryIdentifierWriter`, `LibraryIdentifierChange`, `LibraryIdentifierEdit`,
`LibraryIdentifierEditRule`, `LibraryIdentifierWrite`, `LibraryMetadataLookup`) are gone.
`JellyfinAdapter` implements it, and the words `ProviderIds`, `RemoteSearch` and `Apply` stay in
Infrastructure, no Core name naming the product. The one request that changes anything is Apply,
sent on a second named client (`jellyfin-identify`, a 20-minute timeout) chosen through an optional
client name on `HttpIntegrationAdapter.CallAsync` — one hook in the Template Method, the shared client
keeping its 15 seconds; the film or series search is a `POST` that changes nothing. Neither the item
update (`POST /Items/{id}`) nor the refresh (`POST /Items/{id}/Refresh`) is sent any more.
`LibraryItemIdentifierIdentifiesOnlyTests` allows exactly one verb, `IdentifyAsync`, and forbids any
member named for refreshing, updating, editing, replacing, deleting, images, titles or locks. The tests
the two sections above name are replaced: the registry's by
`IntegrationAdapterRegistryTests.Find_of_the_optional_item_identifier_returns_only_a_library_that_implements_it`,
the kind contract's by `AdapterKindContractTests.Jellyfin_identifies_items_and_every_item_identifier_is_a_media_library_that_lists_items`
and `AdapterKindContractTests.Every_item_identifier_identifies_only_films_or_shows_and_says_why_it_keeps_the_rest_in_the_app`,
and `JellyfinAdapterIdentifiersTests` and `JellyfinAdapterMetadataLookupTests` by
`JellyfinAdapterIdentifyTests`, `JellyfinAdapterIdentifyConfirmationTests`,
`JellyfinAdapterIdentifyFailureTests`, `JellyfinAdapterIdentifyLibraryTests`,
`JellyfinAdapterIdentifyRefusalTests` and `JellyfinAdapterIdentifyShortcutTests`; the second client is
pinned by `IntegrationsInfrastructureExtensionsTests.The_identify_client_has_the_twenty_minute_timeout_and_the_shared_jellyfin_client_keeps_fifteen_seconds`.

## A library's poster (added 0.2.1, MAM-35)

**The library provider reads an item's poster.** `IMediaLibraryProvider` gains
`GetPosterAsync(settings, sourceItemId, posterTag)`, answering a `LibraryPoster` — the bytes and their
media type, a JPEG the library resized to at most 342 pixels wide — or `NotFound` when the library has
no such image or no longer knows the item; an id or a tag the library never gives is a caller's
mistake that throws before anything is sent. `LibraryItem` gains `PosterTag`, the library's own name of
the item's current poster image, which changes whenever the image does: always the item's own, never
its series' or season's lent to an episode, and `null` when it has none. `LibraryPosterNames`
(`Core/Library`) says which file names the app serves a poster under: the item id and the tag, each 1
to 64 letters and digits, and `.jpg`
([ADR 0027](0027-a-library-works-poster-is-the-librarys-own-image-read-by-the-syncs-tag-and-served-by-the-app.md)).

**A member of the kind contract, not an optional capability.** Reading the library is what a library
provider does, so every implementer serves it — `JellyfinAdapter`, and the test doubles — and nothing
asks the registry whether it may. It is never a member of `IMediaLibraryItemIdentifier`:
`LibraryItemIdentifierIdentifiesOnlyTests` scans that capability and every type of
`Core.Abstractions.Library` its members reach — `LibraryIdentifyRequest`, `LibraryIdentification`,
`LibraryIdentifyOutcome`, `LibraryIdentifyRefusal` — for image words, and reaches none of
`IMediaLibraryProvider`, `LibraryItem` and `LibraryPoster`. No Core name names the product
(`CoreIsVendorNeutralTests`): the poster and its tag are Core's words, while `ImageTags`, `Primary`,
`format=Jpg` and `quality=90` stay in Infrastructure. The Jellyfin adapter's fetch is one GET on the
shared `jellyfin` client, with its 15 seconds and its 4 MiB limit, never on Apply's
`jellyfin-identify`, through `HttpIntegrationAdapter.CallAsync` like every other call — the key masked
out of any sentence, no redirect followed —, asking for `image/jpeg` instead of JSON and keeping only
a JPEG.

`JellyfinAdapterPostersTests.GetPoster_asks_for_the_items_primary_image_as_a_342_wide_jpeg_with_the_key_in_the_media_browser_header`,
`JellyfinAdapterPostersTests.GetPoster_goes_through_the_shared_client_and_its_fifteen_seconds`,
`JellyfinAdapterPostersTests.GetPoster_with_an_id_jellyfin_never_gives_throws_without_sending_anything`,
`JellyfinAdapterPostersTests.GetPoster_with_a_tag_the_sync_never_reads_throws_without_sending_anything`,
`JellyfinAdapterPostersTests.ListItems_reads_no_poster_tag_for_an_episode_without_its_own_image_whatever_its_series_has`
and `LibraryPosterNamesTests.TryParse_refuses_any_other_name` pin it;
`LibraryItemIdentifierIdentifiesOnlyTests.No_member_is_named_for_refreshing_images_titles_locks_deleting_replacing_or_editing`
holds as it was.

## A download client's whole listing (added 0.3.0, MAM-24)

**`IDownloadClient` gains one read, `ListJobsWithFilesAsync(settings)`: every job the client has, in
any state and destination, each with its files
(`ClientJobWithFiles(ClientJob Job, IReadOnlyList<ClientJobFile>? Files)`, `Files` `null` while the
client doesn't know them yet).** It is the one member that answers with a whole client, and [ADR
0031](0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md)
says why: the Download dialog marks an old copy a torrent lists before the owner schedules its
replacement. It is used only behind the replacement gates (ADR 0033), never to follow a job, which
keeps its own reads (ADR 0014). A member of the kind contract, not an optional capability: every
download client implements it, today the qBittorrent adapter alone. Read whole or not at all — a job
the answer can't name, or files it can't read, fail the call, as one left out could hide a held copy
—, and a client too old to list the files with the jobs answers `UnsupportedVersion`, never jobs
without files, which would read as nothing held.

**The vendor's words stay in Infrastructure.** The qBittorrent adapter sends the sign-in,
`app/version` and one `GET /api/v2/torrents/info?includeFiles=true`, with no filter and never
`includeTrackers`, and refuses a qBittorrent older than 5.2.0 (`HoldingsMinimumVersion`:
`includeFiles` came with Web API 2.11.8 and 5.2.0 ships 2.15.1; an older one ignores the parameter
without a word). Its checked session now hands back the version it read, as reported and as
compared, so the gate costs no second `app/version` read; the job lookup, the files read and the
submission pass it by. An answer over the 4 MiB cap fails with the adapter's own sentence, passed to
`HttpIntegrationAdapter.CallAsync` as its optional oversize error. `FakeQBittorrentServer` lists
`has_metadata` and emits `files` only from 5.2 on; no Core name names the product
(`CoreIsVendorNeutralTests`).

`DownloadClientNeverRemovesTests` scans the new member with the others — still no member named for
removing, deleting, stopping or pausing —, and
`QBittorrentAdapterJobsWithFilesTests.ListJobsWithFiles_signs_in_reads_the_version_and_asks_once_for_every_torrent_with_its_files_with_the_session`,
`QBittorrentAdapterJobsWithFilesTests.ListJobsWithFiles_of_a_qbittorrent_older_than_5_2_is_an_unsupported_version_and_asks_for_no_torrents`,
`QBittorrentAdapterJobsWithFilesTests.ListJobsWithFiles_of_a_torrent_without_metadata_has_its_files_unknown_never_none`,
`QBittorrentAdapterJobsWithFilesTests.ListJobsWithFiles_of_a_torrent_without_a_usable_hash_fails_the_whole_answer`,
`QBittorrentAdapterJobsWithFilesTests.ListJobsWithFiles_of_an_answer_larger_than_the_app_reads_fails_in_its_own_words`
and
`IntegrationsInfrastructureExtensionsTests.A_whole_listing_of_torrents_with_their_files_logs_nothing_from_the_http_client`
pin it.

## The download client's words, and what a library is told happened (added 0.3.0, MAM-26)

**The move of an old copy adds no member to `IDownloadClient` — its one write is still `SubmitAsync`,
and its member names may now also not move, relocate, rename or set a location —, and the optional
capability `IMediaLibraryFileLookup` says what happened to each path it asks a library to look at
([ADR
0031](0031-a-replacement-never-touches-a-torrent-and-an-old-copy-a-torrent-lists-waits-until-the-owner-removes-the-torrent.md),
[ADR
0030](0030-a-replaced-copy-is-renamed-into-a-quarantine-on-the-same-mount-and-stays-there-until-the-owner-restores-it-or-deletes-it-for-good.md)).**

**The words.** `DownloadClientNeverRemovesTests` forbids, beside removing, deleting, stopping and
pausing, the verbs move, relocate and rename as whole words of a name, and the consecutive words set
and location, as ADR 0031 asked since a `SetLocationAsync` would pass word by word; set, save and
path and set, download and path are our addition, qBittorrent's other ways to put a torrent's files
elsewhere. The method keeps its name, which ADR 0014 and the roadmap cite.

**The kind of change.** `RequestScanAsync` takes a `LibraryFileChange`, `Created` or `Deleted`, from
Core's library abstractions; any other value is a caller's mistake that throws before anything is
sent. Jellyfin's adapter sends it as each path's `UpdateType`, which Jellyfin 12.2 doesn't read —
its monitor looks at the path either way — but which says what happened; the check of new files
sends `Created`, as it did, and the move and Restore send `Deleted` and `Created`.

**The write contract is no service.** `IFileQuarantine`, like `IFileInventory`, is the host's own
file system: one implementation, registered by the files' infrastructure extensions as a singleton,
no adapter type and no product name; `CoreIsVendorNeutralTests` scans it with the rest of Core.

`DownloadClientNeverRemovesTests.No_download_client_member_is_named_for_removing_deleting_stopping_or_pausing`,
`DownloadClientNeverRemovesTests.The_detector_flags_a_removing_verb_and_spares_an_ordinary_name`,
`JellyfinAdapterFilesScanTests.RequestScan_of_paths_that_went_posts_each_as_deleted_and_is_taken_on_204`,
`JellyfinAdapterFilesScanTests.RequestScan_of_a_change_that_is_no_member_is_a_callers_mistake_and_sends_nothing`,
`FakeJellyfinServerFilesTests.Media_updated_records_each_requests_update_types_beside_its_paths`
and `FilesInfrastructureExtensionsTests.AddFilesInfrastructure_registers_the_local_quarantine_as_one_singleton`
pin it, and `CoreIsVendorNeutralTests` the contract's names.
