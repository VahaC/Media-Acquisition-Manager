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
