# 28. Duplicates are worked out on every read, in four classes — one file, one hash, one registry key, or one title and year no id contradicts — and a group only shows

Status: accepted (0.2.1, MAM-20)

## Context

Specification §3.3 names four classes: the same file or a hardlink (one device and inode), a
byte-identical duplicate (one full hash; the recoverable space estimated "with hardlinks/snapshots
in mind"), another copy of the same work (a reliably matching TMDb or IMDb id, different files,
never called redundant) and a possible match (title, year, duration and other clues where ids are
missing, confirmed by hand and never a reason to remove). Different cuts, SDR and HDR are
distinctions, not junk, and the report offers no bulk deletion. The earlier decisions set what each
class may read. Two names are one file only by the identity the app read
([ADR 0006](0006-every-title-identifier-and-file-the-core-stores-names-its-source.md)); works are
grouped per source by ids only, so two integrations' copies of one film are two works
([ADR 0008](0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md)); a
title and a year match only with that title's own year, with no distance and no year ± 1, and
ADR 0012 rejected a list of edition words for matching
([ADR 0012](0012-a-search-or-a-release-matches-a-library-work-by-identifier-then-title-and-year-then-title-alone-and-every-match-is-listed.md));
an episode is known by its own IMDb and TVDB ids or its series' ids with its numbers, a show's TMDb
id never matches a film's, and "another copy of the same work" needs a registry id the files share
([ADR 0017](0017-an-episode-is-a-work-known-by-registry-ids-and-the-library-alone-says-what-an-item-is.md));
seeding is no evidence and a file's space counts once per identity
([ADR 0018](0018-a-file-is-a-duplicate-whatever-a-torrent-client-does-with-it-and-its-space-counts-once-per-identity.md));
the scan's names sit beside the libraries' in `PhysicalFiles`, belong to no work, and a name no
library reports is told only at read time
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md));
a hash is kept under the identity, size and modification time the app read
([ADR 0022](0022-a-file-is-hashed-only-when-a-file-of-another-identity-shares-its-size-and-could-be-its-copy-as-the-last-step-of-the-inspection-one-at-a-time.md));
and an id the owner linked counts like the library's, inside the library whose items it links,
joining two libraries' copies left to MAM-20
([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).

The reference library, read from the owner's development database on 2026-10-07 (last sync that
morning, nothing written), makes the obvious shapes go wrong:

- **A work with several files is rarely a plain copy.** 17 movies hold two files or more: 9 are
  different cuts or editions (Термінатор 2's three files are a Special Extended Edition, a
  Remastered Theatrical Cut and a Special Edition; Чужий a Theatrical and a Director's Cut), 3 are a
  short and its "(Widescreen)" variant, one holds a bonus documentary as a version, one — "Той, хто
  біжить по лезу" — holds Blade Runner 2049 under the 1982 film's ids, and only 3 are other copies.
- **One IMDb id can sit on two different episodes.** 20 pairs of The Penguins of Madagascar episodes
  share one IMDb id (IMDb lists them as paired segments) under two TVDB ids and two places, and
  Mickey Mouse Clubhouse S03E31 "The Golden Boo Boo" carries S03E33's IMDb and TVDB ids.
- **One episode can be two items.** Eight Mickey Mouse Clubhouse episodes are split into `a` and `b`
  halves, two items with the episode's ids and place: two versions of one work, not copies to thin.
- **One item can hold different works.** Tom & Jerry Kids is one episode item with 65 files, 65
  different cartoons, and no ids.
- **Titles and years are noisy.** By displayed title and year, the two "works" that coincide are the
  Mickey pair and The Continental's three items, which hold three IMDb ids; by any title, 28 groups,
  26 of them Mickey episodes whose original title is another segment's, and 27 of the 28 with
  different ids.
- **Nothing to measure yet.** No file of the development database has an identity, a link count, a
  hash, a duration or a dynamic range, and no two of production's video files share a size (ADR
  0018), so the first two classes and the space estimate rest on the tests.

## Decision

**The Duplicates list groups the library's file names into the four classes of specification §3.3
on every read, from what the database already holds — the names of one device and inode, files of
other identities with one SHA-256, film and episode works that share a registry key across every
library and integration, and works or unreported names that share a title key with its year and
that no kind or id keeps apart — stores nothing, removes nothing and asks no download client,
flags every disagreement for the owner instead of resolving it, and estimates the room a copy
frees only for byte-identical files on file systems known not to share blocks (Vasyl's decisions of
2026-10-07).**

**Worked out on every read, nothing stored.** `DuplicateGrouping` in Core is pure and holds every
rule; `LibraryDuplicateService` only loads — a fixed handful of queries however large the library,
the id lists as one JSON parameter each — and maps. A sync, an inspection, a scan or a link moves the
groups at once, with no migration and no table to keep in step. Each group has a `key`: the first
16 bytes of the SHA-256 of its canonical text, 32 lowercase hex characters —
`SameFile|{device}|{inode}`, `ByteIdentical|{sha256}`, `SameWork|` and its sorted work ids,
`Possible|` and its sorted subject ids —, so it names the group on every read while what makes it
one stays; the Duplicates page (MAM-21) will address groups by it. The classes are independent: a
name can be in a byte-identical group and in a same-work group at once.

**What a name is.** Every `PhysicalFiles` row of a library version, and every scan row no library
reports now (`LibraryAppPaths`, ADR 0021: a scan row stays until the next scan after a sync starts
reporting its path). Rows are one name per path key — the app path in canonical form where the
integration's current mappings give one, else the path the library gave — so one path two libraries
report is one name held by both works, shown by the row the app has looked at, else the first by
service path and id. Its identity, link count and size are the app's last look (`ReadSizeBytes`),
never the size a library reported; its hash counts only under that look's device, inode, size and
modification time, so a hash of bytes changed since never makes a group; its duration and its video
tracks' dynamic ranges count only when the last look read the media and the reading is no failure.

**Same file.** Names grouped by device and inode, two path keys or more. Evidence: the identity,
the highest link count read and the number of names. The room recovered is `0`, with no gap:
removing a name frees nothing until its file's last name goes. A name without an identity is in no
such group.

**Byte-identical.** Names with a current hash grouped by SHA-256, two identities or more, each with
every name it has. The estimate is every copy's size but the largest's, each copy's size the larger
its hashed names read (a hash is never kept without its size, ADR 0022). It is `null`, with the first
reason that applies, when a copy's file system can share blocks (`SharingFileSystem`), else when one
is unknown (`UnknownFileSystem`), else when a copy's link count is unknown or higher than the names
the app knows (`UnlistedNames`: that name keeps the bytes whatever is removed here).

**The file system decides the estimate (Vasyl's decision 3).** `FileSystemSharing` knows ext2,
ext3, ext4, JFS, ReiserFS, vfat, exFAT and NTFS (`ntfs`, `ntfs3`) to give every file its own blocks,
and btrfs, ZFS, XFS, bcachefs, APFS, ReFS, OCFS2 and NILFS2 to be able to share them — reflinks,
snapshots, deduplication —; anything else, an NFS or CIFS share, an overlay, a FUSE file system or a
blank, is unknown, as the server or the layer below may share without saying so.
`IFileInventory.DescribeFileSystemsAsync` names a device's type from the system's mount table: on
Linux `/proc/self/mountinfo`, read once per call, the device's `major:minor` decoded with the inverse
of glibc's `makedev` (`MountTable`), the type taken after the ` - ` that ends the optional fields; a
device whose lines disagree, or no line, is unknown, and so is every device when the table can't be
read and on any other platform — on Windows, where the app is developed, a device id is a volume
serial with no mount table. It reads no library file, takes no allowed root and never throws. The
service asks it only for the devices of byte-identical groups, and only when the list asked for is
of every class or of `ByteIdentical`.

**Same work.** The subjects are the film and episode works holding a name; a series holds no file
and is known through its episodes. Each has registry keys made from its ids as every reader counts
them — MAM-34's links applied through `ILibraryLinkReader`, never the raw rows, where a wrong id a
link replaced still sits (`RegistryKeys`):

- **IMDb is one number space** for films, shows and episodes, so its key has no kind: a film item
  that is an episode and the library's episode with that IMDb id are one work.
- **TMDb and TVDB number each kind apart**, so their keys carry the work's kind: a film's `Tmdb 1399`
  never meets a show's `TmdbTv 1399`, nor a TVDB episode id a TVDB film id.
- **An episode is also its show's key with its place.** For each position with a series and both
  numbers, each of the series' ids with the season and episode, whatever the series item: the
  living room's two Grand Tour series items and the bedroom's one give S02E01 one key.
- **A film item linked as an episode joins the library's own episode.** An episode-to-show link with
  both numbers gives the key `TmdbTv`, the show, season and episode — the key of a real episode of a
  series whose TMDb TV id is that show. The library still says what each item is (ADR 0017): the
  film item stays a film, holds none of the show's ids, and the grouping changes nothing in the
  library; it only shows the two as copies.

Works sharing any key, the value trimmed and compared upper-cased (the `WorkMatching` rule), are one
component, across every library and integration, by union-find. A component is a group when it holds
two different files — each counted once, by its identity where known, else by its path — and two
different copies, a copy being the set of files one version holds: a film stacked in two parts is
one copy, and two libraries' versions of one path, or of one hardlinked file, are one copy. A work
with no key enters no component: the 65 id-less Tom & Jerry Kids files are no copies (ADR 0017), and
a shared item proves nothing. The evidence is the keys two works or more hold, or a single work's
own; the room recovered is `null`, as no copy is ever called redundant (§3.3).

**A disagreement stays one work, flagged for review (Vasyl's decision 1).** A group's distinctions,
and `needsReview` whenever there is one:

- `IdentifierConflict` with the scheme: a number space of the works' own ids holding two values —
  one IMDb id with two TMDb ids, a linked id beside another library's id of its scheme;
- `IdentifierConflict` with the scheme and `ofSeries`: the series keys naming two shows of one
  registry;
- `EpisodeNumbers`: one show, one episode's ids at two places — S03E31 and S03E33, the Penguins
  pairs;
- `Edition`: the files' edition sets differ, a labelled file and an unlabelled one included;
- `DynamicRange`: two files whose measured dynamic ranges are known and differ.

Each file lists its own works with their own ids and sources, so the owner sees which side is which;
nothing is merged, picked or written back. Not "possible", not a fifth class: a shared registry id is
the reliable evidence §3.3 asks for, and what it hides is for the owner to read.

**Edition words label a distinction and decide no match (Vasyl's decision 2).** `EditionLabels`
reads sixteen editions (`MediaEdition`: Theatrical, DirectorsCut, Extended, Unrated, Uncut,
Remastered, SpecialEdition, Imax, OpenMatte, FinalCut, Redux, International, Alternate, Assembly,
Widescreen, FullScreen) in English and Ukrainian ("режисерська версія", "розширена", "без цензури",
"ремастеринг"…), whole words in any letter case, the words of a phrase separated by any run of
spaces, dots, underscores or dashes, the apostrophe of "Director's" any of three or none, from the
file name and from the name the library gives a version (`EditionLabel`). A title of the work or
of its show that itself names an edition — or such a title an unreported name claims — is taken out
first, so "Uncut.Gems.2019.mkv" names no edition. ADR 0012 rejected such a list for matching because it never
ends; that still holds, as this one decides no match and keeps no two works apart: it only labels
copies already grouped by their ids or their title and year, so a word it misses costs a flag, never
a wrong group. The dynamic range is ffprobe's measurement only; "HDR" in a file name is no evidence.

**Possible matches.** The subjects are the film and episode works holding a name and each name no
library reports. A work's keys are its titles with a year, every source's, as `TitleMatchKey` with
that title's own year; an unreported name's are what its file name, then its folder's name, claims
with one year (`FileNameClaims.ReadUnlessEpisode`) — "Aliens (1986)/aliens.mkv" by the folder —, and
none when either names seasons or episodes, as every episode of a show claims the show's one title
and year. Two subjects sharing a key and its year are offered, unless they are kept apart — two
works of different kinds; two works with ids of one number space and none shared (The Continental's
three items); two episodes whose known places, by show title, season and episode, share none
("Episode 1" of two shows) — or are already one: one same-work component, or one file between them
by identity or hash, which the classes above show. A group is the subjects joined by pairs; where
that would hold two subjects kept apart, each group grows instead from a pair no group holds yet by
the partners apart from none of its members, so The Continental's three items, an id-less item and a
scanned "The Continental (2023)" are three groups, the two with each item, never one saying the
three may be one work. The evidence is the titles two subjects or more share and whether the
measured durations agree (`DurationAgreement`: 2 minutes or 10 % of the longest, MAM-34's runtime
tolerance made symmetric); distinctions are `Edition` and `DynamicRange`; `needsReview` is always
true and the room recovered `null`. No distance, no year ± 1, never a title alone.

**The list.** `GET /api/library/duplicates?class=&page=1&pageSize=25`, for every signed-in user as
the Library is: groups by class in the specification's order, then by the title the Library card of
their first work shows, in title order (a group of unreported names by its first path), then by key,
paged on the server; `class` is a class name in any letter case, never a number. Each file is the
Library's own file (`LibraryFileDto`, so MAM-21 reuses its parts) with the identity, size, hash and
editions grouping read, and the works holding it; the owner-only sentences — a path or inspection
problem, the name of who set a link — are `null` for everyone else.

| Rejected option | Why not |
| --- | --- |
| Store the groups, refreshed after each sync, inspection or scan | A migration and a table to keep in step with three writers; a link or a scan would show stale groups until the next refresh |
| Group by the library's items or works only | One item holds 65 different cartoons, and two integrations' copies are two works (ADR 0008) |
| Every registry's key across kinds, or every registry's within the kind | TMDb gives a film and a show one number, so one key across kinds joins them; IMDb numbers every title once, so a key within the kind hides a film item that is an episode |
| Conflicting ids as a possible match, or a fifth class | Vasyl: a shared registry id is reliable evidence; the disagreement is a distinction to review, not a weaker match |
| Resolve a conflict by the newest source or the link | Hides what §3.1 blocks on; the owner says which side is wrong |
| Keep a film item linked as an episode apart from the library's episode | The same show, season and episode is the same work; the library's word on the item's kind stays untouched |
| Read editions only from the library's version names | Jellyfin names versions only on an item with two sources; most cuts are said in file names |
| Read the dynamic range from file names | A claim, not a measurement; ffprobe reads it |
| Estimate the space wherever both sizes are known | On btrfs, ZFS or XFS two copies may share their blocks: the figure would promise room that never comes back |
| Assume ext4 when the mount table says nothing | Unknown beats a guess (Vasyl) |
| A distance between titles, or a year ± 1, for possible matches | ADR 0012: "Aliens" for "Alien", a remake's neighbour |
| Offer an unreported episode by its show's title and year | Every episode of a season claims it: a whole season becomes one "work" |
| Connected components of possible pairs, whatever they hold | The Continental's three items, each paired with one scanned file, would be one group saying they may be one work |
| A count per class in the answer | Each class's `totalCount` comes from asking for it; the page that would show counts is MAM-21's |

`DuplicateGrouping`'s rules are pinned over HTTP, one class per test class.
`LibraryDuplicatesSameFileHttpTests.List_groups_two_hardlink_names_of_one_file_and_counts_its_space_once`,
`LibraryDuplicatesSameFileHttpTests.List_says_removing_a_hardlink_name_frees_nothing`,
`LibraryDuplicatesSameFileHttpTests.List_makes_no_group_of_one_path_two_libraries_report` and
`LibraryDuplicatesSameFileHttpTests.List_leaves_out_a_scanned_name_a_library_reports_now` pin the
first class;
`LibraryDuplicatesByteIdenticalHttpTests.List_groups_two_files_with_one_sha256_and_estimates_the_room_a_copy_takes_on_ext4`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_on_a_file_system_that_can_share_blocks`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_where_the_file_system_is_unknown`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_when_a_file_has_a_name_the_app_never_found`,
`LibraryDuplicatesByteIdenticalHttpTests.List_gives_no_estimate_when_a_files_link_count_is_unknown`,
`LibraryDuplicatesByteIdenticalHttpTests.List_names_a_file_system_that_can_share_blocks_before_a_name_the_app_never_found`,
`LibraryDuplicatesByteIdenticalHttpTests.List_makes_no_byte_identical_group_of_one_file_with_two_names`,
`LibraryDuplicatesByteIdenticalHttpTests.List_makes_no_group_of_a_hash_kept_for_a_file_that_changed_since`
and `LibraryDuplicatesByteIdenticalHttpTests.List_asks_the_file_system_only_of_byte_identical_groups`
the second and its estimate;
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_same_movie_in_two_libraries_by_its_imdb_id`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_two_libraries_copies_by_a_tmdb_id_the_owner_linked`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_copies_by_a_linked_id_in_place_of_the_wrong_one_a_library_gave`,
`LibraryDuplicatesSameWorkHttpTests.List_lists_three_movie_items_with_one_imdb_id_as_one_work_never_redundant`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_the_files_of_one_item_without_ids`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_two_halves_of_an_episode_as_versions_of_one_work`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_episodes_of_a_series_the_library_knows_as_two_series_items`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_a_movie_and_a_series_sharing_a_tmdb_number_apart`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_a_film_a_show_and_its_episode_synced_with_one_tmdb_number_apart`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_a_film_item_and_the_librarys_episode_with_one_imdb_id`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_a_film_item_linked_as_an_episode_with_the_librarys_own_episode`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_one_version_in_several_parts` and
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_one_file_two_libraries_hold_through_hardlinks`
the keys;
`LibraryDuplicatesSameWorkHttpTests.List_flags_two_works_with_one_imdb_id_and_different_tmdb_ids_for_review_and_lists_their_files_apart`,
`LibraryDuplicatesSameWorkHttpTests.List_flags_two_episodes_whose_shows_ids_differ_for_review`,
`LibraryDuplicatesSameWorkHttpTests.List_flags_two_episodes_with_one_episodes_ids_under_different_numbers_for_review`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_different_cuts_apart_and_flags_their_editions_for_review`,
`LibraryDuplicatesSameWorkHttpTests.List_reads_an_edition_from_the_name_a_library_gives_a_version`,
`LibraryDuplicatesSameWorkHttpTests.List_reads_no_edition_from_the_words_of_a_works_title` and
`LibraryDuplicatesSameWorkHttpTests.List_flags_copies_whose_measured_dynamic_ranges_differ` the
distinctions;
`LibraryDuplicatesPossibleHttpTests.List_offers_two_works_without_ids_with_one_title_and_year_as_a_possible_match`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_works_with_a_similar_title_or_another_year`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_works_whose_ids_differ`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_a_movie_and_an_episode_with_one_title_and_year`,
`LibraryDuplicatesPossibleHttpTests.List_offers_a_scanned_file_by_its_folders_name`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_the_episodes_of_a_show_no_library_reports_as_one_work`,
`LibraryDuplicatesPossibleHttpTests.List_offers_a_file_beside_each_work_whose_ids_differ_never_the_works_together`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_episodes_of_two_shows_or_two_places_with_one_title_and_year`
and `LibraryDuplicatesPossibleHttpTests.List_says_whether_the_durations_agree` the possible
matches; and `LibraryDuplicatesByteIdenticalHttpTests.List_classes_a_file_a_torrent_seeds_like_any_other_and_asks_no_download_client`
the client left alone. In Core, `RegistryKeysTests`, `EditionLabelsTests`, `FileSystemSharingTests`,
`DurationAgreementTests` and `FileNameClaimsTests`; in Infrastructure, `MountTableTests` and, on
Linux only, `LocalFileInventoryFileSystemLinuxTests.DescribeFileSystems_of_the_device_a_file_is_on_names_its_type`
and `LocalFileInventoryFileSystemLinuxTests.DescribeFileSystems_names_every_device_asked_in_their_order`.
`LibraryDuplicatesHttpTests` pins the endpoint: who reads it, the sentences, the filter, the paging,
the order and the names on the wire.

## Consequences

**The owner sees every copy and every disagreement, and decides nothing yet.** By these rules each
of the reference library's 17 movies with two files or more is a same-work group, the cuts and the
widescreen variants flagged `Edition` where their names say so, and the eight Mickey halves are
unflagged groups of two versions; the 20 Penguins pairs and S03E31/S03E33 are same-work groups
flagged for review, their files listed under their own works; no movie joins another, as no movie
id sits on two works; and no possible match should be offered, as the coinciding titles have ids
that differ or already make one work. Run on 2026-10-07 through the endpoint in the test host
against a copy of the development database (Jellyfin synced that day, never inspected), it gave
exactly that: 46 same-work groups, 34 of them flagged — 13 movies by `Edition`, the 20 Penguins
pairs by `IdentifierConflict` (TVDB) and `EpisodeNumbers`, S03E31/S03E33 by `EpisodeNumbers` — and
no group of another class; MAM-22 does the live check on production, after an inspection. The
halves' `a`/`b` carry no word the rules read, so those groups show no distinction and the owner
tells them by their paths; the misfiled Blade Runner 2049 is flagged only because its neighbour's
name says "The Final Cut".

**Every request reads the whole library.** A group can join any two names, so the list loads every
name, version, work, title, position and link (one query each) and groups them in memory, then reads
the page's own files' facts; at the reference installation's 1,379 names that is a handful of
queries. A list of one class other than `ByteIdentical` doesn't ask the file system.

**The recommendations read a scanned name the same way.** `FileNameClaims` moved from
`RecommendationRunner` to Core, so a scan-found file keeps a work out of the recommendations (MAM-33)
and is offered as a possible match by one reading. It moved with one fix: a backslash now separates
folders as a slash does — the old call bound `Split('/', '\\', …)` to a count of 92 —, so a
Windows-style path ("D:\Movies\Aliens (1986)\aliens.mkv") claims by its folder's name in the
recommendations too, where it claimed nothing
(`RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_a_scan_found_files_name_claims`).

**Known limits, left on purpose.** On Windows, and wherever the mount table can't name a device —
a nested btrfs subvolume's own `st_dev` —, no byte-identical group there has an estimate
(`UnknownFileSystem`). A film named like an episode ("Star Wars Episode 1 (1999).mkv") is offered as
no possible match by its name; its bytes still group it. A work every title of which names an
edition ("Apocalypse Now Redux") loses that label from its copies' names. A link's show has no
titles in the library, so its place keeps no two episodes apart in the possible matches. A word the
list doesn't know, or a cut no name states, gives no `Edition` flag.

## Anti-consequence

This shows; it doesn't act. Nothing is removed, moved, renamed, written or stored because of a group,
in bulk or one by one; no group is confirmed, dismissed or kept, and no audit row is written — those
are the Duplicates page's (MAM-21), and a replacement is Phase C's (§3.4). A same-work group says
the files are copies of one work by their ids, never that one is redundant; a possible match never
authorises anything (ADR 0012's Anti-consequence). No download client is read: a file a torrent
seeds is classed and counted like any other (ADR 0018), and what removing a seeded copy means stays
MAM-23's. The library is unchanged: a film item grouped with an episode stays a film, and what
Jellyfin says an item is stays its word (ADR 0017).

## Decisions (added 0.2.1, MAM-21)

**A possible match is now confirmed or dismissed, by the owner alone, and that is all that is
stored.** The Anti-consequence above said no group is confirmed, dismissed or kept and no audit row
is written, as those were the Duplicates page's. MAM-21 built the page and gave the owner Confirm,
Dismiss and Undo of a possible match, each change with its audit row
([ADR 0029](0029-a-possible-match-is-confirmed-or-dismissed-by-the-owner-alone-kept-by-its-groups-key-and-never-authorises-removal.md)).
The groups are still worked out on every read and stored nowhere; what is stored is the owner's
decision, one row of `DuplicateMatchDecisions` per group key, the key defined in **Worked out on every
read** above. The page addresses a group by it, as that paragraph foresaw: `PUT` and `DELETE
/api/library/duplicates/{key}/decision`. No other class is decided, and the rest of the
Anti-consequence holds: nothing is removed, moved, renamed or written to the library because of a
group, confirmed or not, and a confirmed match is no same-work group.

**The list.** `GET /api/library/duplicates` takes `decision` — `Confirmed` or `Dismissed` by name, in
any letter case, never a number, checked after the page and the class: "“x” is not a decision. Use
Confirmed or Dismissed." —, and without it leaves the dismissed possible matches out of every class
and of the totals. Each group carries `decision` last, `null` while undecided and for every other
class; `needsReview` stays as these rules set it, so a possible match still needs review once
confirmed. A list with `decision` asks no file system, whatever its class: only a possible match is
ever decided, and it shows no estimate, so the file system is asked only for a list of every class
or of `ByteIdentical` with no decision. The order of **The list** above now follows what a group's
card is headed by: by class, then by that title — an episode's group by its series' title, then its
season (the specials after the numbered seasons, a place without a season last) and its number —,
then by key. By the episode's own title, as MAM-20 ordered it, one show's episode groups came in no
order the page shows and two shows' came between each other. **Every request reads the whole
library** above now reads one table more, the decisions, whole, in one query.

**The page shows no count per class.** Its tabs — All, Same file, Byte-identical, Same work,
Possible and Dismissed — carry their names alone, and a tab's total is its pager's, so the rejected
count per class stays rejected. Each file is shown with the Library's version rows and comparison
table, as **The list** above foresaw, named by the end of its path that tells it from the group's
other files, its works, identity, hash and editions beside it.

`LibraryDuplicatesHttpTests` pins the filter and `decision` on the wire, and
`LibraryDuplicatesHttpTests.List_orders_episode_groups_by_their_series_then_season_and_episode` the
order; `LibraryDuplicatesByteIdenticalHttpTests.List_asks_the_file_system_only_of_byte_identical_groups`
also asks for `decision=Dismissed`, `decision=Confirmed` and `class=ByteIdentical&decision=Confirmed`
and finds no file system asked for them; `LibraryDuplicateDecisionsHttpTests` pins the decisions
(ADR 0029 names its tests).
