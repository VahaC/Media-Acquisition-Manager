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

**Amended 2026-10-08 (MAM-37).** Four rules added below narrow **Same work** to copies of one film
or episode: two episodes of one show at different places are never one component (**Episodes at
different places**), and a component's copies split by the segment letters of an episode's file
names, by the years a film's names claim and by the editions all their names say, each part a group
with a key of its own (**Segments**, **Years, films only**, **Editions**, **Parts and their keys**).
So the list under **A disagreement stays one work, flagged for review** loses `EpisodeNumbers`,
keeps `Edition` for possible matches only and gains `RunningTimes` (**Running times**); and
**Edition words label a distinction and decide no match** still decides no match, but its words now
part copies. Of the tests above,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_two_halves_of_an_episode_as_versions_of_one_work`
is now `LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_an_episodes_a_and_b_files`,
`LibraryDuplicatesSameWorkHttpTests.List_flags_two_episodes_with_one_episodes_ids_under_different_numbers_for_review`
is now `LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_two_episodes_with_one_episodes_ids_under_different_numbers`,
and `LibraryDuplicatesSameWorkHttpTests.List_keeps_different_cuts_apart_and_flags_their_editions_for_review`
is now `LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_different_cuts_of_one_film`.

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
name says "The Final Cut". (MAM-37 since: the Penguins pairs and S03E31/S03E33 make no group by
their places, the `a` and `b` files none by their letters, Blade Runner none by its years and
editions, and the cuts and widescreen variants none by their editions; a copy of production's
database listed 4 groups on 2026-10-08, one flagged by its running times — **Production's copy**
below.)

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
list doesn't know, or a cut no name states, gives no `Edition` flag (MAM-37 since: it leaves two
cuts one group of copies, as **Editions** below).

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

## Episodes at different places (added 0.2.1, MAM-37)

MAM-22's check of production on 2026-10-08 listed 46 groups, all same-work: 34 flagged for review —
13 whose editions differ, the 20 Penguins of Madagascar pairs and Mickey Mouse Clubhouse
S03E31/S03E33 — and 12 without a flag, the eight Mickey Mouse Clubhouse episodes kept in an `a` and
a `b` file among them; that is what **Consequences** above foresaw. MAM-37 began with the running
times (**Running times** below). Shown the list that build gave on a copy of production's database,
still those 46 groups, Vasyl rejected the Penguins pairs: "з назви файлів видно що різні серії
одного сезону" — the file names show different episodes of one season. Decision 1 above kept two
episodes carrying one episode's ids at two places one group, flagged `EpisodeNumbers`; the sync
keeps them two works already (ADR 0008, ADR 0017), and only the grouping's union-find made them one
again. This section and the three after it take his corrections of that day in the order the
grouping applies them.

**Two episodes of one show at different places — another season or another episode — are never one
work of the Duplicates list, whatever ids they share, and neither is a work the library itself holds
at two places of one show; films keep decision 1 (Vasyl's decision of 2026-10-08, reversing decision
1 for episodes).**

- **A show is its ids and its titles.** `ShowPlacesOf` gives each work its places, show by show,
  once per read. A show is every series id and every title (by `TitleMatchKey`) one place names,
  joined across all the works: a series' TVDB, TMDb and IMDb ids and its titles are one show, a
  link's TMDb show is that show when the series holds its id, and a show the library knows by no id
  is still one show by its title — The Penguins of Madagascar's series once had none. Two shows of
  one title are one show here, which can only keep works apart, never join them. An episode is
  placed by its positions; a film only when the owner linked it as an episode (MAM-34), at the
  link's place.
- **No join puts one show's two places together.** `WorkComponents` is now a union-find with
  refusals, so the order of the joins decides who is with whom: the works go in id order, never in
  the order the loader read them. First every pair of placed works sharing a key — every pair, not
  each holder with the first, as a refused first holder must not keep the copies behind it apart —,
  a join refused when the two components hold one show at different sets of places, or either holds
  a show at two; so every placed work of a component holds the component's places, and two places of
  one show never meet, whatever chain of ids runs between them. Then the unplaced works among
  themselves, as before. Last, each unplaced component joins every placed component it shares a key
  with, or none of them when two of those disagree or one is at two places: a film item carrying the
  IMDb id of two episodes may be either, so it is a copy of neither, as the sync's
  `LibraryWorkGrouping` joins an item that fits two works to neither. When nothing disagrees and no
  work is at two places, the components are a plain union-find's.
- **A work at two places is a copy of nothing.** With no show ids to tell them apart, a sync makes
  two episodes one work at two places — S03E31 carrying every id of S03E33 in a show the library
  knows by no id. Such a work joins nothing and nothing joins it, so its component is itself alone,
  and **Same work** drops it without dropping anyone else's copies: two film items carrying its ids
  are still each other's copies.
- **One episode's copies still group, and films are unchanged.** Copies of one episode at one place
  in two libraries are one work, while another episode under its IMDb id stays out. Copies of one
  episode under two shows' ids are at one place when the shows share an id or a title, being one
  show; when they share neither, nothing says they are one show, and they join at any places: one
  group flagged `IdentifierConflict` with `ofSeries`, as before. Two films with one IMDb id and two
  TMDb ids stay one work, flagged.
- **`EpisodeNumbers` is gone.** No group can hold one show's two places, so the distinction, its
  value 2 and the page's label for it are removed; `IdentifierConflict` (1), `Edition` (3) and
  `DynamicRange` (4) keep their values, and the wire writes distinctions by name.
- **Possible matches follow.** Two works kept apart by their places are never offered by one title
  and year (`IsApart`): S03E31 and S03E33, which production's library titles "Donald Hatches an
  Egg", of 2010 both, are neither one work nor a possible match.

The 20 Penguins of Madagascar pairs and S03E31/S03E33 left production's list, and with the three
rules below it went from MAM-22's 46 groups to 4 (**Production's copy** below).

| Rejected option | Why not |
| --- | --- |
| Keep such pairs one group, flagged `EpisodeNumbers` (decision 1, 2026-10-07) | Vasyl: the file names show two episodes of one season; the sync keeps them two works already |
| Know a show by its ids alone | A show the library knows by no id would keep none of its episodes apart |
| Join in the order the loader reads the works | With refusals the order decides the components: the list would hang on a query's order |
| Join each holder of a key to its first holder only | A refused first holder — another episode under the key — would keep the copies behind it apart |
| Drop the whole component a work at two places ends in | Other works' real copies would go with it |
| Join a film item carrying two episodes' id to one of them | It may be either; the sync joins such an item to neither |

`DuplicateWorkComponentsTests` pins the components on production's ids, among them
`DuplicateWorkComponentsTests.WorkComponents_two_episodes_of_one_show_at_different_places_under_one_imdb_id_are_apart`,
`DuplicateWorkComponentsTests.WorkComponents_episodes_of_a_show_without_ids_at_different_places_are_apart_by_its_title`,
`DuplicateWorkComponentsTests.WorkComponents_one_episode_id_in_two_shows_sharing_no_id_or_title_joins_them`,
`DuplicateWorkComponentsTests.WorkComponents_a_film_item_linked_at_one_place_and_the_librarys_episode_at_another_are_apart`,
`DuplicateWorkComponentsTests.WorkComponents_two_films_with_one_imdb_id_and_different_tmdb_ids_stay_one`
and `DuplicateWorkComponentsTests.WorkComponents_gives_the_same_components_whatever_order_the_works_come_in`;
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_two_episodes_with_one_episodes_ids_under_different_numbers`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_two_episodes_of_one_show_at_different_places_under_one_imdb_id`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_one_episodes_copies_and_leaves_another_episode_under_its_imdb_id_out`,
`LibraryDuplicatesSameWorkHttpTests.List_joins_a_film_item_carrying_the_imdb_id_of_two_episodes_to_neither`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_one_work_the_library_holds_at_two_places_of_a_show`
and `LibraryDuplicatesSameWorkHttpTests.List_groups_two_films_carrying_the_ids_of_a_work_at_two_places_without_it`
the list, beside
`LibraryDuplicatesSameWorkHttpTests.List_flags_two_episodes_whose_shows_ids_differ_for_review`,
unchanged; and `LibraryDuplicatesPossibleHttpTests.List_never_offers_two_episodes_with_one_imdb_id_at_different_places_with_one_title_and_year`
the possible matches.

## Segments (added 0.2.1, MAM-37)

The same correction took in the Mickey Mouse Clubhouse episodes kept in two files,
`Mickey Mouse Clubhouse_S03E09a_-_Road Rally …` and `Mickey Mouse Clubhouse_S03E09b_-_Road Rally …`.
Jellyfin gives both one episode's numbers and ids, so the sync makes them one work of two versions
(ADR 0017), and **Same work** listed each such episode as a group of two copies with no flag, the
owner left to tell them by their paths (**Consequences** above). Vasyl, 2026-10-08: the `a` and `b`
files are not duplicates.

**An episode's files whose names carry different segment letters are no copies of each other: a
component's copies split by the letter their files' names glue to the first `SxxEyy`, so an `a` file
is a copy of another `a` file only — never of the `b` file, nor of the whole episode —, while the
Library still shows the episode as one work of two versions, as Jellyfin does (Vasyl's decision of
2026-10-08).**

- **Read narrowly.** `EpisodeSegments.Read` takes a lowercase `a` to `d` glued to the file name's
  first `SxxEyy` — season up to three digits, episode up to four, as `EpisodeMarker` reads them —,
  standing on its own: no letter or digit after it ("S03E09a1080p"), and no range or pair, a joiner
  and then another letter, an episode number or a marker ("S03E09a-b", "S03E09a-10",
  "S03E09a-S03E09b"). Not `e`, a multi-episode name's next marker ("S01E01e02"); not a capital,
  likelier a title's first letter ("S01E05A Day at the Beach"); "S03 E09a", "1x09a" and "E08a" are
  read as nothing, and a later marker says nothing. A letter read by mistake can only keep two
  copies apart, never make a group.
- **A copy's letters are its files'.** Every name's letter is read as the list loads it, from its
  file name alone, and only this class uses it; a copy carries the letters of all its files' names,
  and each set of letters is a part. A whole episode's file carries none, so it is no copy of an `a`
  file.
- **A name still decides nothing about an item.** ADR 0017's rejected "Read `SxxEyy` from a file
  name to make an item an episode" holds: the letter makes no item an episode, joins no work and
  changes nothing the Library shows; it only parts copies their ids made one.

The eight Mickey Mouse Clubhouse episodes left production's list. A copy of a segment in another
library is still its copy: a group per letter, each with its own files and running times (**Running
times** below) under a key of its own (**Parts and their keys** below).

| Rejected option | Why not |
| --- | --- |
| Keep the `a` and `b` files one group of two versions, unflagged | Vasyl: they are not duplicates |
| `EpisodeMarker`, MAM-34's prefill reader | Its boundary rejects a letter glued to the number |
| A capital letter, an `e`, or a range | A title's first letter, a multi-episode name's next marker, two segments in one file |

`EpisodeSegmentsTests` pins the reading;
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_an_episodes_a_and_b_files`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_each_segment_of_an_episode_with_its_copy_in_another_library`,
`LibraryDuplicatesSameWorkHttpTests.List_gives_each_segment_group_its_own_running_times` and
`LibraryDuplicatesSameWorkHttpTests.List_keeps_an_episodes_whole_file_apart_from_its_segment_file`
the list.

## Years, films only (added 0.2.1, MAM-37)

Production's «Той, хто біжить по лезу» is one movie item of two versions,
`[01] Blade Runner (1982) [The Final Cut] UHD-BDRip 1080p H.265 …` at 7,051,384 ms and
`[02] Blade Runner 2049 (2017) [US Transfer] BDRip 1080p H.265 …` at 9,827,808 ms, under the 1982
film's IMDb tt0083658 and TMDb 78: Jellyfin made the sequel's file a version of the first film's
item. Decision 1 rightly makes one work of it by its ids, and MAM-22's list showed it flagged by
"The Final Cut" alone. After the next local check Vasyl said "Blade Runner 2049 взагалі не має бути
в дублікатах" — it must not be among the duplicates at all —, and of «Ніж у воді»'s film and
featurette, "це дубль": that one is. What tells the two apart is the years their names claim: a rule
put to him with the change, not his words.

**A film's copies whose file names claim years more than one apart are no copies of each other: a
component's copies split by the clusters of the years their names claim alone in brackets, and
copies that claim none are copies of the others only while the claimed years make one cluster; an
episode's copies are never split by years (the rule for Vasyl's decision of 2026-10-08).**

- **A year alone in brackets.** `FileNameYear.Read` takes four ASCII digits from 1880 to 2099 alone
  in round or square brackets — "(1982)", "[1962]", "( 1962 )" — and nothing else. Not a year that
  shares its bracket with a word or a number: a release's, a remaster's or a cut's as often as the
  film's ("[2001 Redux]" of a 1979 film, "[2019 Remaster]", "[1997 Special Edition]"), a title's
  ("(2001 A Space Odyssey)") or a resolution's ("[1920 x 1080]"), so production's
  `Seksmisja [1983 DVDRemux].mkv` claims none. Not a bare year, a title's as often as a release's
  ("Blade Runner 2049", "1917"), as the header search takes only a bracketed one (ADR 0015); not a
  list or a span, "(2004, 2007)", "(1940-1967)", a collection's; and a name whose brackets claim two
  different years claims none. The bounds are fixed, not the clock's year and one: the grouping
  reads no clock, so a name reads the same every year, and a group the years split keeps its key
  from one New Year to the next.
- **A run of years one apart is one film.** A copy's years are those its files' names claim — a copy
  in parts normally claims one —, and a copy whose names claim years further apart than one is taken
  as claiming none. The years the copies claim are sorted and cut where two neighbours are more than
  one apart, a festival and a release, a year's end and the next being one film's: 1982, 1983 and
  1984 are one cluster, as a cut anywhere in that run would part two copies a year apart. One
  cluster or none splits nothing, so a copy whose name claims no year — «Ніж у воді»'s featurette —
  stays a copy of the film, key and all. With two clusters or more, each is a part, its years in its
  key, and the copies that claim none, which neither can take, a part of their own, "none".
- **Films only.** In an episode's name a year alone in brackets is its show's or its season's —
  "S01E01. MobLand (2025). …", Sonarr's `{Series TitleYear}` —, never its own, and its place and
  letters already say which episode it is: a component holding an episode, or a film linked as one,
  is never split by years.
- **Not ADR 0012's year ± 1.** That refuses a match by title and year; here the ids made the match,
  and a year apart only fails to part it.

Blade Runner left production's list by its years, and by its editions too, The Final Cut beside none
(**Editions** below). Seksmisja (1984) stays one group: `Seksmisja (1984).mp4` claims 1984 and the
remux none.

| Rejected option | Why not |
| --- | --- |
| `ReleaseWorkClaims.Read`, which `FileNameClaims` reads a name by | It too takes a year alone in brackets, but without one it reads a bare year the scene way: "Blade.Runner.2049.1080p" would be of 2049 |
| A year beside a word or a number in its bracket | A release's, a remaster's or a resolution's year would part one film's copies: "[2001 Redux]" of a 1979 film |
| Copies whose years are equal only | A festival and a release, a year's end and the next, are one film's |
| Windows from the first year claimed | A cut anywhere in 1982, 1983 and 1984 would part two copies a year apart |
| The clock's year and one as the last year | The grouping reads no clock: a name would read otherwise next year, and a key change at New Year |
| Episodes too | An episode's bracketed year is its show's or its season's |

`FileNameYearTests` pins the reading, production's names among its cases;
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_a_films_versions_whose_names_claim_years_far_apart`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_two_films_under_one_id_whose_names_claim_years_far_apart`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_a_films_versions_one_group_when_a_year_shares_its_bracket_with_a_word`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_copies_whose_names_claim_a_run_of_years_one_apart_one_group_under_its_key`,
`LibraryDuplicatesSameWorkHttpTests.List_takes_a_copy_whose_parts_claim_years_far_apart_as_claiming_none`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_a_copy_whose_name_claims_no_year_with_the_films_copy_under_its_key`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_copies_by_the_years_their_names_claim_and_leaves_one_claiming_none_out`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_copies_claiming_no_year_beside_two_clusters_as_a_part_of_their_own`
and `LibraryDuplicatesSameWorkHttpTests.List_keeps_an_episodes_copies_one_group_whatever_years_their_names_claim`
the list.

## Editions (added 0.2.1, MAM-37)

Last that day, of `[05.1] Rambo. Last Blood (2019) [Theatrical Cut …]` and
`[05.2] Rambo. Last Blood (2019) [Extended Cut …]` "та інші схожі" — and others like them —, Vasyl:
"це НЕ ДУБЛІ!!!!! бо різні версії", not duplicates, different versions. Specification §3.3 asks for
cuts to be shown as distinctions and never treated as junk, and decision 2 above flagged a same-work
group `Edition` when its files' sets differed: 13 of MAM-22's 46 were flagged so.

**Different editions are not duplicates: a component's copies split by the set of editions their
names say, so a same-work group holds copies of one cut only and never carries `Edition`, while a
possible match still shows "Editions differ" and one file's names and byte-identical files are
untouched (Vasyl's decision of 2026-10-08, reversing decision 2's flag, and specification §3.3's
reading that different editions need review, for copies of one work).**

- **The whole set decides.** A copy's editions are all its names say — its files' names and the
  libraries' names of its version, read by `EditionLabels` as decision 2 reads them, over all its
  files —, so Terminator 2's "UK Remastered, Theatrical Cut" is not another copy's "Theatrical Cut".
  Widescreen, FullScreen, Open Matte, IMAX and Remastered are editions too, so Tom & Jerry's
  "Feedin' the Kiddie" and its Widescreen copy are no copies of each other; and a copy that says
  none beside one that says one is another edition, as the `Edition` flag always took it: nothing
  says it is that cut. These edges were stated to Vasyl with the rule.
- **One file says one thing.** The names of one file say one thing between them, all that any of
  them says: a hardlink one library names by its cut and another by none is that cut through both,
  so its names never land one file in two parts. The letters and years above are read the same way,
  once per file.
- **One set splits nothing.** Two copies of one cut, said two ways, are one group under the key the
  works always had; with two sets or more, each set is a part, named by its editions in
  `MediaEdition`'s order, or "none".
- **Bytes are bytes.** One file's names and byte-identical files are other classes, untouched: their
  bytes are one, whatever their names say. A possible match keeps `Edition` beside `DynamicRange`:
  there it is evidence for the owner deciding whether two works are one.
- **The words now part copies.** `EditionLabels` still decides no match (ADR 0012) and joins
  nothing, but a word it misses now leaves two cuts one group of copies, and one it reads by mistake
  takes a real copy off the page.

12 groups left production's list by their editions alone — The Bourne Identity (Theatrical / Open
Matte), Rambo: Last Blood, Terminator 2 (three editions), Terminator Salvation, Alien, Aliens,
Alien³, Alien: Resurrection, AvP: Requiem, and Tom & Jerry's "Feedin' the Kiddie", "The Egg and
Jerry" and "Tops with Pops" (plain / Widescreen) —, and Blade Runner by its editions as well as its
years.

| Rejected option | Why not |
| --- | --- |
| Keep the cuts one group, flagged `Edition` (decision 2, §3.3's distinctions) | Vasyl: different versions, not duplicates |
| An unlabelled copy as a copy of the labelled one | Nothing says it is that cut |
| Copies sharing one edition of their sets as copies | "UK Remastered, Theatrical Cut" is not "Theatrical Cut" |
| Part one file's names, or byte-identical files, by their names | Their bytes are one |
| Drop `Edition` from the possible matches too | It is evidence for the owner deciding whether two works are one |

`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_different_cuts_of_one_film`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_the_theatrical_and_the_extended_cut_of_one_film`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_three_editions_of_one_film`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_a_cartoon_and_its_widescreen_copy`,
`LibraryDuplicatesSameWorkHttpTests.List_makes_no_group_of_a_copy_whose_version_name_says_a_cut_beside_one_that_says_none`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_two_copies_of_one_cut_under_the_works_key`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_copies_of_one_cut_apart_from_another_cut_under_a_key_of_their_own`,
`LibraryDuplicatesSameWorkHttpTests.List_groups_the_copies_saying_no_edition_beside_another_edition_under_a_none_key`,
`LibraryDuplicatesSameWorkHttpTests.List_names_a_part_whose_copies_say_several_editions_by_them_in_edition_order`,
`LibraryDuplicatesSameWorkHttpTests.List_reads_an_edition_from_the_name_a_library_gives_a_version`,
`LibraryDuplicatesSameWorkHttpTests.List_reads_no_edition_from_the_words_of_a_works_title` and
`LibraryDuplicatesSameWorkHttpTests.List_puts_one_file_whose_hardlinked_names_say_different_editions_in_one_group_of_copies`
pin it;
`LibraryDuplicatesPossibleHttpTests.List_flags_a_possible_matchs_files_whose_editions_differ` and
`LibraryDuplicatesHttpTests.List_writes_classes_distinctions_and_editions_by_name` the possible
match's `Edition`, and, in the web app, `DuplicateGroupCard.test.tsx`'s "flags it when its files'
editions differ" its badge.

## Parts and their keys (added 0.2.1, MAM-37)

**A component's copies split in the order of the sections above — by segment letters, then, for
films only, by clusters of years, then by sets of editions, each split within the parts of the one
before: which segment, which film, then which cut —, and a part is a group when it holds two copies
and two files.** Copies and files are counted as **Same work** above counts them, and what a file's
names say is worked out once per file, so every version holding it reads it alike. A part's works
are those holding its copies; its distinctions are worked out on its own works and files, and its
running times on its own copies. Its key is the hash of `SameWork|` and its sorted work ids, as
before, then `|Segments:` and its letters when its copies carry letters, then `|Years:` and its
years ("1982", or "none") and `|Editions:` and its editions ("Theatrical,Remastered", or "none")
only when those parted something. A component whose copies carry no segment letter and that
nothing splits is one part, the whole of it, so its group, key and all, is what it was before
MAM-37 — production's four groups kept the keys MAM-22's list gave them —, and two libraries' `a` and `b` groups of the same works keep a key each. The
groups are still worked out on every read and stored nowhere; the list reads nothing more, as every
name's path was loaded already.

## Running times (added 0.2.1, MAM-37)

MAM-22's check recorded two same-work groups that are one work only by the ids the library gives, as
**Consequences** above foresaw for one of them: «Ніж у воді» (1962), `Nóż w wodzie (1962).mkv`,
5,731,959 ms by ffprobe, beside the featurette `Polanski+Skolimowski o Noże.mkv`, 1,600,699 ms, both
under IMDb tt0056291 and TMDb 11502, with no flag; and Blade Runner, off the list now by its years
(**Years, films only** above). The ids are right to make each one work (decision 1), but nothing
told the owner to look. This was MAM-37's first rule, and «Ніж у воді», which Vasyl later called "це
дубль", is the group it flags.

**A same-work group whose shortest measured copy runs under three quarters of its longest gets the
distinction `RunningTimes`, beside any other — and so `needsReview` —, and every same-work group in
which two copies or more have a running time carries the shortest and the longest as evidence,
flagged or not (Vasyl's decision of 2026-10-08).**

- **Copies, not files.** A copy is what one version holds, the set of its files, as **Same work**
  above counts copies: two libraries' versions of one path, or of one hardlinked file, are one copy,
  and each distinct set counts once. Its running time is the sum of its files' measured durations,
  so a film stacked as CD1 and CD2 runs as long as both; a file's duration is the longest any of its
  names was measured at, as the names of one file read one file.
- **Each part its own.** The copies compared are one part's (**Parts and their keys** above): an `a`
  file's 11 minutes beside a `b` file's 7 would flag both segments' groups, and each group's running
  times are its own letter's.
- **An unknown decides nothing.** A copy with any file not measured — never inspected, or a reading
  that failed — has no running time, and fewer than two copies with one give no distinction and no
  evidence.
- **Under three quarters, in whole milliseconds.** `RunningTimeSpread.IsWide` is
  `4 × shortest < 3 × longest`, so no floating point rounds a boundary either way; exactly three
  quarters is not under.
- **Beside any other distinction, last.** The list under **A disagreement stays one work, flagged
  for review** above gains `RunningTimes`, and the distinctions stay in kind order, `RunningTimes`
  (5) after `IdentifierConflict` and `DynamicRange`: a featurette given a TMDb id of its own under
  the film's IMDb id is `["IdentifierConflict", "RunningTimes"]`.
- **Copies of one work only.** A possible match keeps `durationsAgree`, 2 minutes or 10 %, and its
  distinctions stay `Edition` and `DynamicRange` (**Possible matches** above); same-file and
  byte-identical groups are unchanged. Running times split nothing and join nothing, nothing is
  written, and no copy is called redundant.

`evidence` gains `runningTimes`, last: `{ shortestMilliseconds, longestMilliseconds }`, `null` with
fewer than two copies that have a running time and for every other class. The Duplicates page shows
"Running times differ" among the warning badges and, under "One work by its ids:", only when the
group has `RunningTimes`, "The shortest copy runs 26:41, under three quarters of the longest,
1:35:32.", both durations as the Library page shows a file's. The list reads nothing more: every
name's measured duration was loaded already.

| Rejected option | Why not |
| --- | --- |
| The possible matches' tolerance, 2 minutes or 10 % of the longest (`DurationAgreement`) | It tells another rip from another cut, and a cut no name states is still a copy here: Alien³'s theatrical cut runs 79.3 % of its Special Assembly Cut, Terminator 2's three 88.0 % at the widest |
| More than 40 minutes apart | Never fires on episodes and shorts, which run less than that whole |
| Leave it out where another distinction flags the group already | Ids that disagree say a library is wrong; the running times say what sits under them |
| Join, split or keep works apart by their running times | Out of scope: the registry ids stay §3.3's reliable evidence, and what they hide is the owner's to read |
| Compare files rather than copies | A film in two parts would be flagged beside its single file, each part half of it |
| Compare every copy of a component, across its parts | An `a` file's group would be flagged for the `b` file's running time |

**The margin.** Three quarters comes from production's own copies: the featurette runs 27.9 % of its
film; Blade Runner 2049, before the years took it off the list, made the Final Cut 71.7 % of it; and
no cut ran shorter than Alien³'s theatrical one, 79.3 % of the Special Assembly Cut. With the years
and the editions parting such copies first, the flag meets copies whose names say nothing to tell
them apart: a featurette, another film under the first one's ids whose name claims no year, a cut no
name states. A featurette at three quarters of its film or more, or a second film nearly as long as
the first, stays unflagged by this rule.

`RunningTimeSpreadTests` pins the boundary — exactly three quarters, a millisecond under, a longest
not divisible by four — and production's ratios on either side;
`LibraryDuplicatesRunningTimesHttpTests.List_flags_a_film_and_a_featurette_under_its_ids_for_review_by_their_running_times`,
`LibraryDuplicatesRunningTimesHttpTests.List_flags_running_times_that_differ_widely_beside_ids_that_disagree`
and `LibraryDuplicatesRunningTimesHttpTests.List_flags_nothing_when_the_shortest_copy_runs_over_three_quarters_of_the_longest`
the rule;
`LibraryDuplicatesRunningTimesHttpTests.List_takes_no_running_time_from_a_copy_never_inspected`,
`LibraryDuplicatesRunningTimesHttpTests.List_compares_the_measured_copies_beside_one_never_inspected`,
`LibraryDuplicatesRunningTimesHttpTests.List_takes_no_running_time_from_a_copy_whose_reading_failed`,
`LibraryDuplicatesRunningTimesHttpTests.List_counts_a_copy_in_two_parts_as_the_sum_of_their_durations`,
`LibraryDuplicatesRunningTimesHttpTests.List_takes_no_running_time_from_a_copy_with_a_part_not_measured`,
`LibraryDuplicatesRunningTimesHttpTests.List_counts_one_path_two_libraries_report_as_one_copy`,
`LibraryDuplicatesRunningTimesHttpTests.List_gives_the_shortest_and_the_longest_of_three_copies` and
`LibraryDuplicatesSameWorkHttpTests.List_gives_each_segment_group_its_own_running_times` the copies
and their evidence; `LibraryDuplicatesPossibleHttpTests.List_gives_a_possible_match_no_running_times_distinction_whatever_its_durations`
the possible matches left as they were;
`LibraryDuplicatesHttpTests.List_writes_classes_distinctions_and_editions_by_name` `runningTimes`
and `RunningTimes` on the wire; and, in the web app, `DuplicateGroupCard.test.tsx`'s "says how long
the shortest and the longest copy run when the shortest runs under three quarters of the longest",
"flags running times that differ widely beside ids that disagree, after them" and "says nothing of
the running times the server gives when they don't differ widely" the badge and the sentence.

## Production's copy (added 0.2.1, MAM-37)

**What a copy of production's database showed.** "Checked against production" here means the build
run locally on a fresh copy of production's database, not a publish: production keeps running the
published 0.2.1 image until Vasyl publishes. On 2026-10-08 at 11:31:03 UTC he ran
`tools/mam37/copy_db.py`, whose one read-only ssh call is `tools/mam22/report.py`'s: `mam.db`
(30,281,728 bytes) with its `-wal` (7,321,272) and `-shm` (32,768), never `.secrets`, from the
container of `vahac/media-acquisition-manager:latest`, digest
`sha256:d9c6149a3dbf0e4924ff0d889873996008425dc3b9c2592c56f373fb07cfa78e` (0.2.1), running since
10:19:47 UTC. The copy passed `quick_check`, held 20 migrations, the last
`20261007173129_AddDuplicateMatchDecisions`, and 1,397 names, 1,396 inspected and measured and one
unreadable as media; in the copy alone the allowed folder `/media` was removed and the four
integrations' addresses set to `http://127.0.0.1:9`. The MAM-37 working tree on top of 27fcd79, run
with `dotnet run -c Release` on the copy, applied no migration; Vasyl signed in himself and looked:
"ніби ок". `copy_db.py --verify`, at the start and after the check, found the 14 tables the
duplicates are read from unchanged; the copy was deleted afterwards.

`GET /api/library/duplicates` as the owner listed 4 groups, all Same work, each under the key
MAM-22's list gave it that morning: The Gods Must Be Crazy (1980), two WEB releases at 6,545,205 and
6,545,206 ms; «Ніж у воді» (1962), the film at 5,731,959 ms and the featurette at 1,600,699
(27.9 %), `RunningTimes`, the one needing review; Seksmisja (1984), `Seksmisja (1984).mp4` and
`Seksmisja [1983 DVDRemux].mkv`, 6,980,120 and 7,005,013 ms; and Alien: Romulus (2024), a 2160p
remux at 7,141,927 ms and a 1080p rip at 7,140,926. All four had `runningTimes`; no group of another
class appeared, and none was dismissed. The page showed "1–4 of 4", «Ніж у воді»'s card with "Same
work", "Needs review", "Running times differ" and "The shortest copy runs 26:41, under three
quarters of the longest, 1:35:32.", the other three "Same work" alone; after a reload too, at
desktop width and at 375 px, with no horizontal scroll and no server error, the console's only
errors the session refresh's 401s before the sign-in and after the reload.

Of MAM-22's 46 groups, the 42 that left are the 20 Penguins of Madagascar pairs and S03E31/S03E33
(**Episodes at different places**), the eight Mickey Mouse Clubhouse episodes kept in `a` and `b`
files (**Segments**), Blade Runner (**Years, films only** and **Editions**) and the 12 cuts and
widescreen copies of **Editions**. Nothing in the library, a file or a download client changed: only
what the Duplicates page lists. Production exercises none of these, so they rest on the tests above:
a copy in two parts, an unmeasured copy or a failed reading beside measured ones, a hardlinked file
whose names say different editions, a whole episode file beside an `a` file, a film item linked as
an episode at another place, the `|Years:` and `|Editions:` keys — no production component splits
into two groups —, and possible matches, of which production has none.

## Plain names, quarantined names and each file's removal verdict (added 0.3.0, MAM-40)

**The page names the classes in plain words, a name in the quarantine counts in no group, and each
file of a group of exact copies or of one work's copies carries whether the owner may remove it
([ADR
0037](0037-a-copy-is-removed-from-the-duplicates-page-into-the-quarantine-on-the-owners-word-through-replacements-gates-and-checks.md));
the classes, their rules and their keys stay as above (Vasyl's decisions of 2026-10-12).**

**Plain names, on the page only.** Vasyl couldn't tell what "Same work" meant. The tabs now read
All, "One file, two names" (`SameFile`), "Exact copies" (`ByteIdentical`), "Same film, different
files" (`SameWork`), "Maybe the same film" (`Possible`), "Kept as they are" (`decision=Dismissed`,
[ADR
0029](0029-a-possible-match-is-confirmed-or-dismissed-by-the-owner-alone-kept-by-its-groups-key-and-never-authorises-removal.md)'s
MAM-40 section) and "Removed copies", the owner's alone (`?tab=Removed`, ADR 0037). A card's badge
has the same words, an episode's group "Same episode, different files", while the tab keeps the
film's. Every card has one line saying what its group is and what can be done: "These files hold
exactly the same bytes. Remove a copy you don't need, or keep them as they are.", "Different files
of the same film, by its ids: another size, quality or source. Compare them, then remove a copy you
don't need, or keep them as they are." (an episode's: "… of the same episode, by its ids: another
size or quality. …"), "One file reached by two names or more (hardlinks): it takes its room once,
so there is nothing to keep or remove." and "Maybe the same film: the titles and years match, but no
ids say so. Confirm it if they are one film, or dismiss it if they aren't; nothing is removed either
way." The API keeps its names: `class` and `decision` take and give the values they did. The MAM-21
section's tabs "All, Same file, Byte-identical, Same work, Possible and Dismissed" read so since.

**A quarantined name counts in no group.** **What a name is** above now leaves out every name a
replacement or a removal took into the quarantine and hasn't given back — its row `Moving`,
`Quarantined`, `Restoring`, `Deleting` or `Deleted` —, matched by its old app path and the device,
inode, size and modification time it moved with, all five against the app's last read of the
library's file, whatever a sync or the scan last saw there; a file put at that path since counts,
and so does one a later download got the inode of a name deleted for good, which device and inode
alone would hide for ever. It costs two queries of the quarantine's own rows, however large the
library. A replacement's old copy now leaves its group at its move too, where it stayed until the
next sync.

**Each file's removal verdict.** On `ByteIdentical` and `SameWork` groups each file carries
`removal: { allowed, reason }`, ADR 0037's stored rules read with the gates and the holds once per
page, for every signed-in user; `null` on the other two classes. It reads the database alone: the
list still asks no download client, and the torrents are the Remove request's.

**The Anti-consequence, since.** "Nothing is removed, moved, renamed, written or stored because of a
group" now has two exceptions, each the owner's word on one group: Keep both stores a decision on
exact copies or one work's copies (ADR 0029's MAM-40 section), and Remove this copy moves the one
file the owner pressed it on into the quarantine (ADR 0037). A group still decides nothing, calls no
copy redundant, and the app suggests none.

**The file system for kept copies.** The MAM-21 section's "A list with `decision` asks no file
system, whatever its class" no longer holds for `decision=Dismissed`: kept exact copies are listed
there with their estimate, so that list asks for their devices as one of every class or of
`ByteIdentical` does; `decision=Confirmed` still asks nothing.
`LibraryDuplicatesByteIdenticalHttpTests.List_asks_the_file_system_only_of_byte_identical_groups`
now finds the devices asked for `decision=Dismissed` too.

`LibraryDuplicatesQuarantineHttpTests.List_counts_no_name_a_replacement_took_into_the_quarantine`,
`LibraryDuplicatesQuarantineHttpTests.List_counts_a_name_that_is_still_or_again_where_it_was`,
`LibraryDuplicatesQuarantineHttpTests.List_counts_a_new_file_put_at_a_quarantined_names_path`,
`LibraryDuplicatesQuarantineHttpTests.List_counts_a_new_file_given_the_inode_of_a_name_deleted_for_good_at_its_path`,
`LibraryDuplicatesQuarantineHttpTests.List_counts_no_name_deleted_for_good_while_its_path_identity_size_and_time_all_match`,
`CopyRemovalHttpTests.Remove_takes_a_group_of_two_copies_off_the_duplicates_list`,
`CopyRemovalHttpTests.Remove_from_a_group_of_three_copies_keeps_the_other_two_as_a_group`,
`CopyRemovalActionsHttpTests.Restore_puts_every_name_back_tells_jellyfin_they_came_back_and_the_copy_counts_in_its_group_again`,
`CopyRemovalVerdictsHttpTests` and `LibraryDuplicatesHttpTests` (`removal` on the wire) pin it, and
`DuplicatesPage.test.tsx` and `DuplicateGroupCard.test.tsx` the names and the lines.
