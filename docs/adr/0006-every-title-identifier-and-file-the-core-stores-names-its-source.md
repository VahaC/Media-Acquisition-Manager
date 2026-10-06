# 6. Every title, identifier and file the core stores names its source

Status: accepted (0.1.0, MAM-7)

## Context

Specification §4 gives the core media works, versions, physical files, releases and persisted jobs,
and MAM-7 creates their tables. The ticket names three assumptions the model must refuse, and the
samples MAM-6 collected show why each would go wrong on the reference installation:

- **A work is a Jellyfin item.** `VendorSamples/Jellyfin/movies-page.json` has two items titled
  "Чужі" (1986) with the same TMDb id 679; one item can also group two media sources. Keyed by the
  item, one movie becomes two works, and a second library could never join them.
- **A release is one file.** `VendorSamples/QBittorrent/torrent-files-multi.json` is a pack of the
  Taken films: one release, several files, several works.
- **A physical file is one copy of the bytes.** A hardlink is a second name of the same bytes, and
  Phase B must count its space once (specification §3.3); a byte-identical copy is a second set of
  bytes.

Sources also disagree. A library and a release source spell a title differently, and two sources
can claim different identifiers for one work; §3.1 wants that conflict to block automatic matching,
so it has to survive being stored. A title or an identifier written onto the work would let the last
source to report it overwrite the others.

## Decision

**Every title, identifier, version, physical file and release is a row of its own that names the
source that reported it, and none of the core's identities is a source's id: a work has no title,
year or library id, a release has no file or work, and a physical file row is one name of a file.**

**Provenance is five plain columns** on `ProvenancedEntity`, which the five tables inherit:
`SourceKind` (library, release search, download client, the app's own file inspection),
`SourceConnectionId` (the integration, a foreign key set to null when it is removed), `SourceName`
(its name at the time, copied like the names of an audit row), `SourceItemId` (the source's own id
of the record, such as a library item id or a Torznab `<guid>` without its `sid`, ADR 0004) and
`ObservedUtc` (when it last reported the fact; `CreatedUtc` is when it was first seen). Plain columns
index and take a foreign key, which a complex type can't. The core stamps them, since it knows which
connection it called; the kind contracts return plain values (ADR 0005).

**A work is only its kind.** Its titles (display or original, each with the year that source gives)
and its IMDb and TMDb identifiers are rows; two sources that disagree are two rows, and the
non-unique index on `(Scheme, Value)` finds every work that claims one id. A library's collection id
is never a work identifier: it would match every movie of the box set.

**A version belongs to a work** and keeps the library item (`SourceItemId`) and the version inside it
(`SourceVersionId`) apart, so two items of one movie and one item with two sources both fit.

**A physical file row is one name.** It holds the path exactly as the service reports it, the size,
and the device and inode once the app has read them itself (MAM-9, Phase B), `null` until then and
never inferred. Rows that share `(DeviceId, Inode)` are hardlinks: one set of bytes, counted once.
The path inside the app is worked out through the integration's current path mappings when it is
needed, never stored: a stored one would go stale when a mapping is edited (ADR 0003).

**A release is a snapshot of one listing**, with no path, no file list and no work; its files are
known only from the client after submission (MAM-14). Its download link is sealed like a credential
(ADR 0007).

**Removing an integration keeps what it reported.** Facts keep their `SourceName` with the link set
to null; a download client's jobs keep `DownloadClientName`; its destinations, which can't be used
without it, go with it. Whether a removed library's snapshot is purged is MAM-8's decision, made in
code, not by a cascade.

| Rejected option | Why not |
| --- | --- |
| The work keyed by the library item | One movie in two items is two works; a second library can't join them |
| Title, year and identifiers as columns of the work | The last source overwrites the others, and the conflict §3.1 blocks on is lost |
| Provenance as a complex or owned type | A complex type can't carry the foreign key; an owned type misbehaves when it is null |
| The app path stored on each file | Stale after a mapping is edited; ADR 0003 makes a mapped path where to look, not proof |
| A file identity as one `"dev:ino"` string | Two numbers index and compare without parsing |
| `Restrict` on a job's client | Forbids removing a client that ever had a job; relaxing it later is a SQLite table rebuild |

`MediaModelPersistenceTests.Two_names_of_one_inode_are_two_files_sharing_an_identity`,
`MediaModelPersistenceTests.Conflicting_identifiers_from_two_sources_are_both_kept` and
`IntegrationsDeleteHttpTests.Delete_keeps_the_facts_it_reported_with_their_source_name` assert it;
`MediaModelPersistenceTests.A_release_is_stored_without_a_work_or_a_file` and
`IntegrationsDeleteHttpTests.Delete_of_a_download_client_keeps_its_download_jobs_with_the_client_name`
pin the release and the jobs.

## Consequences

MAM-8 writes library items as versions and files of works it decides by reliable identifiers
(§3.1), and MAM-12 can show who said what beside each title. Reading "the title" of a work now means
choosing among rows, a display rule MAM-8 and MAM-12 own. Phase B counts space per identity, not per
row. The tables carry Phase A columns only: MAM-8 (snapshots), MAM-9 (inspection), MAM-13 (an
idempotency key), MAM-14 (job files) and Phase B (the hash cache) add theirs in their own
migrations. In MAM-7 only integrations and path mappings are written; the other tables stay empty
until their tickets.

## Anti-consequence

Provenance says who asserted a fact, not that it is true. A reported path is still never trusted for
a write (specification §4), and two rows are the same bytes only by `(DeviceId, Inode)` the app read
itself, never by name, size or title.

## Implementation (added 0.1.0, MAM-8)

**Removing a media library integration purges the snapshot it imported** (Vasyl's decision). In the
same transaction as the removal and its audit row, `LibrarySnapshotStore.PurgeAsync` deletes the
titles, identifiers, versions (their files by cascade, whatever source reported them — whether a
file MAM-9's own inspection records under such a version should outlive it is MAM-9's open point),
files and libraries that name the integration as their source *and* have the source kind
`MediaLibrary`, then every work they leave without a title, identifier or version of any source.
Kept with a null link, the snapshot would
stay on the Library page for everyone with nothing left to refresh or remove it, and the same
Jellyfin added again would import every movie a second time beside the stale copy. It is done in
code, as the Decision said, not by a cascade: the foreign keys still set null, so what any other
kind of source reported keeps the MAM-7 rule (a release source's releases, a download client's
jobs, each with the name), and so do the library's sync runs, which are history with the
integration's name copied. The Integrations page's confirmation says what goes for a media
library.

`IntegrationsDeleteHttpTests.Delete_keeps_the_facts_it_reported_with_their_source_name`, named in
the Decision above, is gone. `IntegrationsDeleteHttpTests.Delete_of_a_media_library_purges_its_snapshot_and_keeps_its_sync_runs_with_its_name`
removes one of two synced Jellyfin libraries and finds the other's rows whole, and
`IntegrationsDeleteHttpTests.Delete_of_a_release_source_keeps_its_releases_with_the_source_name`
keeps the MAM-7 rule pinned for the sources that aren't libraries.

**The display rule.** A work's title on the Library page is its first display title in Ukrainian
alphabetical order, letter case ignored (`LibraryTitleOrder`); its original title is the first
original one, and its year comes with the chosen title, else with the original. The list is ordered
by that same title. How titles from several sources sit beside a release stays MAM-12's.

**What a library may store as a file.** A version whose media source isn't one local video file —
a DVD or Blu-ray folder, a `.strm` link, a remote stream — is stored without a file, since a file
row is one name of a file, not a folder or an address; a movie stacked from several files gets a
row per part on its one version. A collection id (`TmdbCollection`) is never read as a work
identifier. `JellyfinAdapterItemsTests.ListItems_gives_a_media_source_that_is_not_a_local_video_file_a_version_without_a_file`
and `JellyfinAdapterItemsTests.ListItems_reads_the_imdb_and_tmdb_ids_and_never_a_collection_id`
pin both. Which items are one work, and how the rows keep their ids across syncs, is
[ADR 0008](0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md).

## Implementation (added 0.1.0, MAM-9)

**The open point is settled: the inspection writes no file rows of its own.** What the app's last
look at a name found — its status, the sentence, when, the identity and link count it read — is
written on the library's own `PhysicalFiles` row, which a sync never touches beyond what MAM-8 lists,
and what the media inspector read is kept in `MediaInspections`, keyed by the file's identity, size
and modification time, which no name owns. So a version a sync removes takes only names with it, and
a file moved under a new name within one sync finds its reading again; a file gone for a whole sync
loses it at the inspection that sync queues
([ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)).
A reading needs no provenance columns: every one is the app's own, so its table names its source.
`ProvenanceSourceKind.FileInspection` stays unused until a name that no library reported is stored
(Phase B's scan). Rows sharing `(DeviceId, Inode)` are now real: both names of a hardlinked file carry
the identity the app read, and `LinkCount` says how many names the file has.

## Implementation (added 0.1.0, MAM-10)

**A search writes its releases (Vasyl's choice: kept in the database between the search and the
download).** Every release a search finds becomes a new `Releases` row: source kind `ReleaseSearch`,
the integration's id and its name as it is when the row is written (read again in the same
transaction, so a source removed meanwhile writes nothing), the listing's id, and the time it was
seen as both `CreatedUtc` and `ObservedUtc`. Its download link is sealed to the row (ADR 0004,
ADR 0007). A new row every search, never an update of an earlier one: a download job points at the
release it sent, and that evidence must not change when the listing is found again. So a release id
names one search's snapshot, and the same listing found again is a different id; MAM-13 recognises a
release it already sent by its source and id (or its info hash), not by the row.

**What the source id may be.** A Torznab guid is stored only when it is the release's own page link,
without the session (`https://toloka.to/t699400`), or a plain token; anything else — the tracker's
download link that Prowlarr writes as the guid of an indexer without one, a magnet — is stored as
`sha256:` and its digest, stable from one search to the next. A listing without a guid takes the page
link as its id.

**Unused releases go after a day.** Each search removes, in one statement, the releases stored more
than 24 hours earlier that no download job points at. A release source's rows keep the MAM-7 rule when
the source is removed: the foreign key sets null, the name stays, and the rows go with the next day's
removal unless a job holds them.

`ReleaseSearchHttpTests.Search_stores_each_release_with_its_source_and_its_download_link_sealed_without_the_key`,
`ReleaseSearchHttpTests.Search_again_stores_new_releases_and_keeps_the_earlier_ones` and
`ReleaseSearchHttpTests.Search_removes_releases_older_than_a_day_that_no_job_sent` pin it, with
`ReleaseSearchSourcesHttpTests.Search_of_a_source_removed_while_it_was_searched_leaves_it_and_its_releases_out`;
`IntegrationsDeleteHttpTests.Delete_of_a_release_source_keeps_its_releases_with_the_source_name` stays as
it was.

## Implementation (added 0.1.0, MAM-12)

**How titles and ids from several sources sit beside a release.** The MAM-8 section left it to
MAM-12, and the Consequences' "who said what beside each title" is now shown. A release is never
joined to a work. The search page shows each matching work as the Library page's card, the display
rule above unchanged, and beside it what matched, each with who said it: the identifier, with every
source that reported it — two sources holding the same id are two entries, never a conflict — and
the title that matched, with its kind (displayed or original), its year and its source; of several
titles that match, one of the year searched for first, then a displayed one before an original,
then the Library's title order. A scheme on which the work's own rows disagree, or on which it
disagrees with the id searched for, is flagged as conflicting and never merged: a work two sources
give two IMDb ids shows both, and the warning. What a release claims stays the release's: the
titles and year read from its title, and the ids its listing gives, stored on its own row
(`ImdbId`, `TmdbId`), whose provenance columns say which source claimed them. The match itself is
never stored
([ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md)).

**A release row keeps more of its listing, and still no work.** Besides those ids, a release row
keeps its seeders and peers, its 0-based place in its source's answer and, for a stored search, the
search source that found it (`SearchSourceId`)
([ADR 0011](0011-a-search-is-stored-once-and-every-filter-sort-and-page-reads-what-it-stored.md)).
That link sets null when the search is removed: a release a download job sent outlives its search,
as the job's evidence. The Decision holds — no path, no file list, no work — and a search still
writes new rows, never an update of earlier ones. A search source copies its integration's name
like every row here, so removing the integration keeps who answered the search.

`SearchesLibraryMatchingHttpTests.Create_lists_every_source_that_holds_a_matched_id_without_calling_it_a_conflict`,
`SearchesLibraryMatchingHttpTests.Create_lists_a_work_two_sources_give_two_imdb_ids_with_the_conflict_and_both_ids`,
`SearchesLibraryMatchingHttpTests.Create_by_both_ids_lists_each_matched_id_and_the_title_of_the_year_asked_for`
and `SearchesLibraryMatchingHttpTests.Create_answers_a_works_card_exactly_as_the_library_page_shows_it`
pin what sits beside a work; `SearchReleasesMatchingHttpTests.Releases_lists_the_ids_each_listing_gives_and_keeps_them_with_the_release`
the release's own claims; and `SearchPersistenceTests.A_release_a_download_job_keeps_outlives_its_search`
and `SearchPersistenceTests.Deleting_an_integration_keeps_the_searches_sources_with_their_names` the
rows.

## Implementation (added 0.1.0, MAM-13)

**The idempotency key, in its own migration.** As the Consequences planned, `AddDownloadJobRequests`
gives `DownloadJobs` its MAM-13 columns: `RequestId`, the id the browser made for the request, unique
per user (`IX_DownloadJobs_RequestedByUserId_RequestId`); `RequestedByUserId`, a foreign key to
`Users` that sets null when the user is deleted, with `RequestedByUserName` copied beside it — the
pattern the job already follows for its client and destination; `ErrorOrigin`, whose sentence a
failed job's error is, which decides who may read it; and `InfoHashV1` and `InfoHashV2`,
the hashes worked out from the payload that was sent — both, as a client names a hybrid torrent by
either
([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).
The key to `Users` rebuilds the table on upgrade, the concern ADR 0009 weighed against a key on
`PhysicalFiles`; it is accepted here because no build before MAM-13 wrote a job, so the copy is
empty. A deleted user's jobs stay with their name, and their request ids no longer collide with
anyone's, as SQLite counts no two nulls the same.

**A release already sent is recognised by its source's id or its info hash, not by the row
(MAM-13's decision 3).** The MAM-10 section's rule, applied: since every search writes its own copy
of a listing, a send is refused with 409 ("This release was already sent to “qBittorrent”, so it
isn't sent twice.") when a job that didn't fail — anyone's — already sent the same client a release
with the same source and the source's id for it, or with the info hash this listing states, on that
release or worked out from what that job sent. Each side counts only when the listing has it: two
listings without an id are not one release by that. A failed job sent nothing new and doesn't count.
The release a job sent still can't be removed while the job exists, so it outlives the day's prune
and its search, as the MAM-12 section says.

`DownloadJobPersistenceTests.A_second_job_for_one_users_request_id_is_refused` and
`DownloadJobPersistenceTests.Deleting_a_user_keeps_their_jobs_with_their_name_and_frees_their_request_ids`
pin the columns, and
`DownloadsIdempotencyHttpTests.Create_of_a_release_already_sent_to_the_client_is_409_and_sends_nothing`
the rule.

## Implementation (added 0.1.0, MAM-14)

**A job's files, in their own migration, named by their job.** As the Consequences planned,
`AddDownloadJobTracking` adds `DownloadJobFiles`: one row per file of a job as its client last listed
it — its place in the client's list (`Position`, unique per job), its path, its size, its progress
and whether the client downloads it — deleted with its job. The Decision said a release's files are
known only from the client after submission; they are stored against the job, never the release,
since another client, or the same one later, may list the same release differently. A row names its
source through its job, which keeps the client's name: the path is the client's own, relative to the
job's folder and exactly as the client spells it, and is neither a service path nor an app path, so no
path mapping applies to it and it never becomes a `PhysicalFiles` row. The job itself gains `Progress`
and `BlockReason` with `BlockReasonOrigin`, whose sentence the reason is — the client's or the app's —
which decides who may read it, as `ErrorOrigin` does
([ADR 0014](0014-a-download-is-followed-by-reading-its-client-and-cancelling-it-only-stops-the-following.md)).
The migration adds columns, a table and indexes and rebuilds no table.

`DownloadJobPersistenceTests.A_jobs_files_keep_their_paths_exactly_and_sizes_past_four_gigabytes`,
`DownloadJobPersistenceTests.A_second_file_in_one_place_of_a_job_is_refused_and_another_job_may_use_it`,
`DownloadJobPersistenceTests.Deleting_a_job_deletes_its_files_and_no_other_jobs` and
`DownloadJobPersistenceTests.A_job_keeps_its_progress_and_its_block_reason_with_whose_sentence_it_is_by_name`
pin the rows.

## Implementation (added 0.2.0, MAM-32)

**An episode's place is a fact with its source.** MAM-32 adds another table on `ProvenancedEntity`:
`MediaEpisodePositions`, one row per library item that is an episode — the episode work, the series
work its series item landed on, and the season and episode numbers the library gives — stamped like
a title and found again by its item, so two items of one episode each place it and two sources that
disagree both stay
([ADR 0019](0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).
The Decision holds: a work is still only its kind, a series is a work with titles and ids like a
movie's, and the numbers are never columns of the episode work. The library's series id is never a
work identifier, as a collection id isn't: one show can be several series items. The schemes gain
`Tvdb` and `TmdbTv`, a show's TMDb id, which never meets a movie's `Tmdb`
([ADR 0017](0017-an-episode-is-a-work-known-by-registry-ids-and-the-library-alone-says-what-an-item-is.md)).
A position goes with its episode work, points at no series once its series work goes, and goes with
the rest of a media library's snapshot when the library is removed (the MAM-8 section).

**A release row says what its listing is.** It gains `Kind` (`Movie` or `Tv`, from the categories
its source lists it in), `CategoryIds` and `TvdbId`, and its `TmdbId` is a show's on a TV release,
told by `Kind`
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
Still no path, no file list and no work. The migration adds columns, tables and indexes and rebuilds
no table.

`LibrarySnapshotTvHttpTests.Sync_of_the_tv_samples_places_each_episode_in_its_series_work_with_its_numbers_and_provenance`,
`LibrarySnapshotTvHttpTests.Sync_after_a_series_item_is_gone_keeps_its_episodes_without_a_series`,
`IntegrationsDeleteHttpTests.Delete_of_a_media_library_purges_its_series_episodes_and_their_positions`
and `ReleaseSearchTvHttpTests.Search_lists_the_movie_releases_then_the_tv_releases_and_stores_each_with_its_kind_and_categories`
pin it.

## Implementation (added 0.2.0, MAM-18)

**The app's own file names are stored at last, with no version.** The MAM-9 section kept
`ProvenanceSourceKind.FileInspection` unused until Phase B's scan; MAM-18 writes it. A file the scan
of the media folders finds that no library reported gets one `PhysicalFiles` row of that kind, with
no source connection, name or item id, no library, no reported size and no version:
`MediaVersionId` is nullable now, null only on these rows, as which work such a file is, if any, is
not the app's to guess from its name (ADR 0017). A library's name always has its version and still
goes with it; the scan's names go with no version, no integration and no purge, only with a later
scan
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md)).

**Their `ServicePath` is the app's own path, which the Decision's reason allows.** The Decision never
stores the app path because a stored one would go stale when a mapping is edited (ADR 0003). On these
rows the app is the reporting source: no connection's mappings apply (`PathMapper` maps a path with no
pairs as it is), so no edit can make it stale: the path as its source reported it is the app's own,
in canonical form. Every reader still maps every name through its connection's current pairs, these
included, and a library's name keeps its service's path.

**Every name gains what the disk said of its size and last write.** The inspection writes
`ReadSizeBytes` and `ReadModifiedUtc` with the identity and link count on every name it looks at, a
library's as well as the scan's: null until read, and null again when the last look found nothing
there. `SizeBytes` stays the size a library reported, and is null on the scan's names. The migration
(`AddFolderScan`) rebuilds `PhysicalFiles` for the nullable column, the rebuild
[ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)
avoided, accepted in its MAM-18 section.

`FolderScanRunHttpTests.Scan_finds_every_media_file_stores_a_name_for_each_one_no_library_reported_and_reads_none`,
`FolderScanPersistenceTests.A_name_without_a_version_is_stored_and_kept_when_a_librarys_version_goes`,
`FolderScanPersistenceTests.A_name_without_a_version_is_kept_as_it_is_when_an_integration_goes`,
`FolderScanInspectionHttpTests.Inspect_writes_the_size_and_time_the_disk_said_on_a_librarys_name_too_and_clears_them_once_the_file_is_gone`
and `FolderScanIsolationHttpTests.Scan_of_a_name_whose_path_holds_a_works_title_changes_nothing_the_library_shows_or_finds`
pin it.

## Implementation (added 0.2.0, MAM-19)

**The hash cache, in its own migration.** As the Consequences planned, `AddFileHashes` adds Phase
B's hash cache: `FileHashes`, one row per set of bytes the app read whole — the file's device, inode,
size and modification time, unique together, the SHA-256 and when it was read — and the hashing
counts of `FileInspectionRuns`. Like a reading, a hash is no `ProvenancedEntity`: every row is the
app's own, so its table names its source, and no name points at it, as a name's hash is the row its
own read identity, size and time name
([ADR 0022](0022-a-file-is-hashed-only-when-a-file-of-another-identity-shares-its-size-and-could-be-its-copy-as-the-last-step-of-the-inspection-one-at-a-time.md)).
The migration adds a table, an index and columns and rebuilds no table.

**Space per identity, as the Consequences said.** The Library page counts the room the files take
from the names' `(DeviceId, Inode)` and the size the app read, one file per identity, never per row
(ADR 0018). The Anti-consequence holds with the hash: two rows are one file only by
`(DeviceId, Inode)`; two files with one SHA-256 hold identical bytes in two places, a byte-identical
copy that takes its space twice, not a hardlink.

`FileHashPersistenceTests.A_second_hash_of_the_same_identity_size_and_modification_time_is_refused`,
`FileHashPersistenceTests.An_unsigned_device_id_past_long_MaxValue_round_trips_bit_for_bit`,
`FileHashPersistenceTests.A_names_hash_is_the_row_its_read_facts_name_to_the_tick` and
`LibrarySpaceHttpTests.Sources_count_two_hardlink_names_of_one_file_in_a_library_once` pin it.

## The catalog's facts and a source's newest releases (added 0.2.1, MAM-33)

**A fifth source kind.** `ProvenanceSourceKind` gains `Metadata` (5): what a metadata catalog
reported. The catalog's parent rows — `CatalogWorks`, `CatalogGenres`, `CatalogCollections`,
`CatalogPeople` and `CatalogLookups` — are `ProvenancedEntity` rows of that kind: the TMDb
integration, set to null when it is removed, its name copied, and as `SourceItemId` the catalog's own
id of the record, `movie/118340`, `tv/73586`, `collection/284433`, `person/24` or `genre/movie/16`, or
for a lookup its cache key, `Movie|imdb|tt0071562` or `Series|title|YELLOWSTONE|2018`; `ObservedUtc`
is when the catalog last said it and `CreatedUtc` when it first did. Their child rows — a work's
genres, people, seasons and recommended works, a collection's parts, a person's works — are part of
their parent's report, carry no provenance of their own and go with it. Removing the integration keeps
the rows with its name, as the Decision says of every fact
([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)).

**The Decision holds for the library.** A catalog work is not a library work: a work is still only
its kind, and no catalog row writes a title, an identifier or anything else onto `MediaWorks`,
`MediaWorkTitles` or `MediaWorkIdentifiers`, nor points at them. Two sources that disagree still
disagree where they are stored: Jellyfin's id on the library's work, TMDb's answer in the catalog.

**A source's newest releases go with their source.** `SourceFeedEntries` keeps a release source's
newest releases ([ADR 0024](0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md)):
a `ProvenancedEntity` of kind `ReleaseSearch`, unique by its source and `SourceItemId`, the listing's
id as the MAM-10 section stores a release's, with its title, description, kind, categories, publish
time and, once resolved, the catalog work it most likely is; a listing without an id is left out. A
read again updates what the source says of it, its `SourceName` and `ObservedUtc`, and keeps its
`CreatedUtc`, when the app first saw it, and what it was resolved to; an entry no source listed for
30 days is forgotten. Unlike every other fact, it goes with its integration: the key to its source
cascades, as a feed is nothing without the source that publishes it, and a source added again starts
its feed afresh. The Decision's "Removing an integration keeps what it reported" holds for every
other row. An entry is no release: it keeps no download link and no job can point at it.

**A release row says what its listing describes.** `Releases` gains `Description` (1000 characters):
the listing's own description when it says more than the title — Toloka's topic title, Ukrainian
title first, where Prowlarr's title keeps the English part alone —, `null` when it gives none and on
every row stored before 0.2.1. Still no path, no file list and no work. `AddReleaseDescription` adds
that one nullable column and `AddRecommendations` creates its tables and indexes; neither rebuilds a
table.

`RecommendationCatalogCacheHttpTests.Recompute_stores_the_catalogs_rows_apart_from_the_library_with_their_provenance`,
`RecommendationCatalogCacheHttpTests.Recompute_never_writes_the_librarys_works_titles_or_identifiers`
and `ReleaseSearchHttpTests.Search_answers_and_stores_each_releases_description_when_it_says_more_than_the_title`
pin it, with `RecommendationFeedHttpTests.Read_keeps_each_release_once_and_a_second_read_adds_none_and_keeps_when_it_was_first_seen`
and `RecommendationFeedHttpTests.Read_forgets_the_entries_no_source_listed_for_30_days` for the
entries. That an entry goes with its source is the database's cascade, which
`RecommendationPersistenceTests.Deleting_a_release_source_deletes_its_feed_entries_and_keeps_another_sources`
pins; `RecommendationFeedHttpTests.Read_skips_a_source_removed_during_the_read_and_keeps_what_the_others_listed`
pins that a source removed in the middle of a read gets no entry stored.

## The owner's word (added 0.2.1, MAM-34)

**A sixth source kind, named in columns of its own.** `ProvenanceSourceKind` gains `Person` (6): an
id a person set by hand in the app, the owner's TMDb link of a library item
([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
Its source is no integration, so a link is not a `ProvenancedEntity`: its `SourceConnectionId` and
`SourceItemId` would name the library item it points at, not who said it. A `LibraryItemLinks` row
names its target — the media library integration and its item id — and its source apart: who set it
(`SetByUserId`, set to null when the user is deleted), their sign-in name copied like the names of an
audit row (`SetByUserName`, as `DownloadJobs.RequestedByUserName`), and when (`SetUtc`, again at each
replace). The IMDb id TMDb gives for the linked film or show keeps TMDb as its source beside it: the
integration (set to null when it is removed), its name copied and when TMDb said it. A reader shows a
link's ids as identifier rows of kind `Person` — the setter's name as their source to the owner alone,
`null` to everyone else — and the IMDb id as one of kind `Metadata`, rows made up for reading and
never saved; the API's identifiers gain `sourceKind` and `setUtc`.

**The Decision holds for the library.** No link writes a title or an identifier onto `MediaWorks`,
`MediaWorkTitles` or `MediaWorkIdentifiers`: a sync still stores what Jellyfin reported, and two
sources that disagree are still two claims — Jellyfin's id on the work, the owner's on the link —
shown side by side as a conflict.

**Removing an integration.** The Decision's "Removing an integration keeps what it reported" is
about facts a source reported; a link is no such fact but the owner's word about one of a library's
items, so removing a Jellyfin integration removes its links and their file identities with it (Vasyl's
decision 12), its audit rows staying with their names. Removing TMDb keeps the links' IMDb ids with
its name; deleting a user keeps their links with theirs; a work a sync deletes leaves its links
unattached, never deleted.

**An audit row names the ids it changed.** `AuditEventIdentifiers` holds, per audit row, the ids
before and after a link's change, write or restore (role, scheme, value, place), and goes with its
row.

`LibraryLinksPersistenceTests.Deleting_a_library_deletes_its_links_and_their_files_and_keeps_another_librarys_and_the_audit_rows`,
`LibraryLinksPersistenceTests.Deleting_the_catalog_keeps_the_links_imdb_id_with_its_sources_name`,
`LibraryLinksPersistenceTests.Deleting_a_user_keeps_their_links_runs_and_none_fits_marks_with_their_names`,
`LibraryLinksPersistenceTests.Deleting_a_work_leaves_its_links_unattached_with_their_files`,
`LibraryLinksPersistenceTests.Deleting_an_audit_row_deletes_its_identifiers_and_keeps_another_rows`,
`LibraryWorksLinksHttpTests.Works_lists_a_linked_id_with_who_set_it_and_the_imdb_id_tmdb_gave_for_the_owner`,
`LibraryWorksLinksHttpTests.Works_lists_a_linked_id_without_the_name_of_who_set_it_or_the_write_error_for_a_member`
and `LibraryLinkSyncHttpTests.Resync_keeps_a_link_on_its_item_and_its_work_and_stores_none_of_its_ids_as_jellyfins`
pin it.

**Amended 2026-10-06 (ADR 0026's Identify section, added in 0.2.1).** "The ids before and after a link's
change, write or restore" now also names the ids of an Identify: `AuditEventIdentifiers` rows hang on
`LibraryItemIdentified` (23) — the item's registry ids as read before an Apply and after it, or the ids
meant to be left when the read again never came — one row per Apply that left the app. Nothing is
restored any more: `LibraryIdsRestored`, `LibraryIdsWritten` and `LibraryMetadataLookupRequested` are
history, never written again, and their enum values stay so the old rows still read.
