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
