# 3. Service paths reach the app through an explicit prefix mapping, never an assumed match

Status: accepted (0.1.0, MAM-6)

## Context

The app has to know that the file qBittorrent finished, the file Jellyfin lists and the file it
inspects are one file. Specification §4 prefers "identical in-container mount paths across
services", and the README and `docker-compose.yml` told the installer to mount the media folders
"at the same paths Jellyfin and qBittorrent see them at".

MAM-6 looked at the reference installation on 2026-09-29: a Proxmox LXC (`media-stack`, Debian 13,
Docker 29.8) with one ext4 filesystem on a 14 TB disk mounted at `/mnt/media14tb`. One host folder
holds every media library and every download, and Jellyfin, qBittorrent and the app see it under
two names:

| Service | Sees `/mnt/media14tb/jelly_qbit_data/Torrent` as | Its folders there |
| --- | --- | --- |
| Jellyfin 12.1 | `/media` | libraries `/media/movies`, `/media/cartoons`, `/media/documentary`, `/media/tv-shows` |
| qBittorrent 5.2.3 | `/downloads` | categories Movies, Cartoons, Documentary, TV Shows save to `/downloads/movies` and so on |
| This app | `/media` | the same as Jellyfin |

So the rule the docs gave can't be followed: Jellyfin and qBittorrent disagree with each other, and
the app can share a path with only one of them. Making them agree means remounting qBittorrent at
`/media` and relocating its 352 torrents (351 complete: 300 seeding, 51 queued to seed).
Specification §1 says the app must work without reorganizing the owner's folders.

The same look answered the rest of specification §7 question 1. Each qBittorrent category folder is
a library folder, so torrents download straight into the library, and none of the files MAM-6 listed
there had a second link. Whether every library file still belongs to a live torrent was not
measured, and Radarr, which runs in the same LXC, was not inspected: its mounts and how it imports
files are unknown.

## Decision

**Each integration declares how its paths map onto the app's own mounts, as prefix pairs (here
qBittorrent `/downloads` → `/media`); the default is the identity, the app never infers a mapping,
and a path that no pair covers, or that two pairs map differently, blocks automatic replacement.**

**The app mounts media where Jellyfin sees it.** Library items are what the user browses and picks
to replace, and they are stored with Jellyfin's paths; downloads cross into the app once per job,
through qBittorrent. Following Jellyfin keeps the translation at that one boundary.

**A mapped path is where to look, not proof.** Whether two paths name the same file is still decided
by device and inode, rechecked before anything is touched (specification §3.4, check 5). The mapping
only gives the app a path it can open.

| Rejected option | Why not |
| --- | --- |
| Remount qBittorrent at `/media` | Relocates 352 torrents on a working, seeding setup; the app must fit the owner's folders, not the other way round |
| Mount the folder into the app twice, at `/media` and `/downloads` | Every file gets two paths, a scan walks it twice, and a rename from one mount to the other fails with `EXDEV` although both are the same filesystem, a trap for the quarantine move of Phase C |
| Guess the mapping from matching tails (`movies/…/X.mkv`) | Works here only because category and library folders share names, and misfires silently where they don't; specification §4 forbids silent guesses |
| Correlate by file identity alone | The app can't read the device and inode of a file it has no path to |

No code implements the mapping yet. MAM-7 stores it with each integration's settings and MAM-11's
destination tests pin it, starting from `VendorSamples/QBittorrent/categories.json` and
`VendorSamples/Jellyfin/virtual-folders.json`, which disagree exactly as the table above shows.

## Consequences

**Destinations can be checked (MAM-11).** The category Movies saves to `/downloads/movies`, which
maps to `/media/movies`, the location of the Jellyfin library "Фільми", so the expected destination
is known without a save path. A category whose folder maps outside every library can still receive a
download, but automatic replacement is off for it (specification §3.2).

**Quarantine can be atomic (Phase C, question 1).** A rename is atomic only within one mount of one
filesystem. Inside the app that is `/media`, so the quarantine folder must be under `/media` but
outside every library location (for example `/media/.mam-quarantine`), where Jellyfin doesn't scan.
A quarantine on its own mount would turn every move into a copy.

**Replacing a library file may touch a seeding torrent (Phase C, question 6).** Where the old copy
is a torrent's own data, moving it makes qBittorrent report missing files and stop seeding it, and
specification §3.4, check 6 says not to touch seeding data. With the library and the downloads in
the same folders that is the common case here; Phase C has to decide what replacement means for it,
and ROADMAP keeps question 6 open.

**The deployment docs change.** The README and `docker-compose.yml` now say to mount media where
Jellyfin sees it, read-only, and that the download client's view will be a mapping. The reference
installation mounts it read-write today; nothing in Phase A writes there, but `:ro` makes sure.

## Anti-consequence

This doesn't let the app trust a path an indexer or a torrent's metadata suggests (specification
§4), and it doesn't make a mapped path safe to change: every write in Phase C still goes through the
§3.4 checks.

## Implementation (added 0.1.0, MAM-7)

**The pairs belong to the integration.** Each Jellyfin or qBittorrent integration keeps its ordered
pairs (at most 20), edited in its Add or Edit dialog and saved by that dialog's one Save; a type that
reports no file paths, such as a Torznab feed, takes none. A path is read by its own shape, never by
the platform the app runs on: `/a/b` is Linux-style, `D:\a\b`, `D:/a/b` and `\\server\share\a`
Windows-style. Repeated and trailing separators collapse, `.` and `..` are refused rather than
resolved, and the pair is stored in that canonical form.

**Matching is folder by folder**: `/downloads` covers `/downloads/movies` but never `/downloads2`.
Letter case counts in a Linux path and not in a Windows one; names are not Unicode-normalised.

**Pairs must agree, and the longest prefix never wins on its own.** At save, a folder mapped twice,
service paths of two styles, and nested pairs that send one folder to two places are refused with a
sentence naming both; nested pairs that agree, two service folders mapped onto one app folder, and
Linux folders that differ only in letter case are accepted. Nested pairs from Windows service paths
onto Linux app paths are refused even when they agree as typed: the service's path matches in any
letter case, the outer pair carries the case it reports into a Linux path, where case counts, and
the inner pair gives its path as typed, so `D:\Torrents` → `/media` with `D:\Torrents\movies` →
`/media/movies` would send `D:\Torrents\Movies\x.mkv` to two places. At use, `PathMapper` applies
every pair that covers the path: one place is `Mapped`, two are `Ambiguous` (a row older than these
rules, or a hand-edited database). With no pairs a path is the `Identity`; with pairs, a path none covers is
`NotCovered`, not the identity. The path inside the app is worked out when it is needed and never
stored (ADR 0006), so an edited mapping leaves nothing stale. Nothing calls `PathMapper` yet; MAM-8
and MAM-11 are its first users, and anything without an app path keeps automatic replacement off.

`PathMapperTests.Map_with_pairs_that_disagree_is_ambiguous_and_names_both_places` and
`PathMapperTests.Map_of_the_adr_0003_samples_puts_every_qbittorrent_category_inside_a_jellyfin_library`
(`categories.json` through `/downloads` → `/media` lands in `virtual-folders.json`'s libraries) pin
it, and `IntegrationsPathMappingsHttpTests.Create_with_nested_path_mappings_that_disagree_is_400_naming_both`
the refusal at save. `PathMappingRulesTests.Validate_with_nested_windows_service_paths_mapped_to_posix_app_paths_is_the_rule`
pins the Windows-onto-Linux refusal, and
`PathMapperTests.Map_with_nested_windows_service_pairs_onto_posix_paths_is_ambiguous_in_another_letter_case`
the ambiguity it prevents.

## Implementation (added 0.1.0, MAM-8)

**`PathMapper` has its first users, and both read it when asked.** The Library's works list maps
every file a sync stored through the reporting integration's pairs as they are at that moment
(`LibraryMapper`): each file comes with its `appPath`, its `pathStatus` and, without an app path,
the `pathProblem` sentence naming its path. The sources list counts each library's files without
an app path (`filesWithoutAppPathCount`) the same way, and gives the reason of the first. Both
reasons go to the installation owner only, as they quote the mappings, which are the owner's
settings; everyone else gets the status and the count. Nothing of it is stored: the design first
kept the count on the sync run, where the first edit of the mappings would have made it disagree
with the list. So adding or fixing Jellyfin's pairs changes the paths
and the warning at the next read, without a new sync.

**Which Jellyfin library holds a file is decided by service paths alone.** At sync time
`LibraryLocationMatcher` compares the file's path as Jellyfin reports it with the root folders of
Jellyfin's libraries, folder by folder; one covering library is the file's, while none or two
(nested roots) leave it without one, never a guess. Jellyfin's server-wide path substitution
rewrites the paths of media sources but not the libraries' roots, so such files have no library:
they are still listed, and only the library filter leaves them out.

Mounting media where Jellyfin sees it still holds, and makes every library path the identity on
the reference installation. Where the app mounts it elsewhere, the owner adds pairs to the Jellyfin
integration, and the Library page flags every file they don't cover. Automatic replacement, which
needs an app path, is still Phase C's; MAM-11 is the mapper's next user.

`LibraryWorksDetailsHttpTests.Works_maps_each_file_through_its_integrations_path_mappings` and
`LibraryWorksDetailsHttpTests.Works_maps_paths_through_the_mappings_as_they_are_now` pin the read
time mapping,
`LibrarySourcesHttpTests.Sources_counts_files_without_an_app_path_with_the_mappings_as_they_are_now`
and
`LibrarySourcesHttpTests.Sources_counts_the_files_the_current_path_mappings_give_no_path_and_says_why_to_the_owner_only`
the count and who reads its reason, and
`LibraryLocationMatcherTests.Find_of_a_file_under_the_roots_of_two_libraries_is_null` the refusal
to guess a library.

## Implementation (added 0.1.0, MAM-9)

**The file inspection is the mapper's next user, before MAM-11.** A run maps every stored file name
through its reporting integration's pairs as they are when the run starts, the way the works list
does, and looks only at the app path that comes out; a name the pairs give none is recorded as
`NoAppPath` with the mapper's sentence, and the inventory never sees it. The mapped path is still
where to look and nothing more: the inspection reads the device and inode itself, inside the allowed
roots only, every link resolved
([ADR 0009](0009-the-app-reads-a-file-only-inside-the-allowed-roots-and-keeps-each-reading-by-the-files-identity.md)),
and those decide which names are one set of bytes. Fixing a mapping shows on the next run, which the
owner can start from the Library page.

## Implementation (added 0.1.0, MAM-11)

**Destinations are checked, as the Consequences planned.** `DestinationPathCheck` (Core, pure) takes
the folder qBittorrent says a category saves to, maps it through the qBittorrent integration's pairs,
and requires the app path to lie inside the app path of a Jellyfin library's root folder, folder by
folder (`NormalizedPath.IsUnder`), the root itself included. Each root folder is mapped through its
own Jellyfin integration's pairs, so both sides keep their own mappings. On the samples, Movies'
`/downloads/movies` is `/media/movies`, the folder of "Фільми", and the other three categories land in
their own libraries. A folder that only holds a library folder (`/media` above `/media/movies`) is
outside it: what lands there isn't in the library.

**The library root folders are stored now.** MAM-8 read them at sync time and kept none. The sync now
stores each library's folders as Jellyfin reports them (`LibraryLocationFolders`, replaced by every
sync, gone with the library and with the integration,
[ADR 0008](0008-a-library-sync-replaces-its-sources-snapshot-only-after-reading-all-of-it.md)) and
never their app path: the check maps them when it looks, like everything else here, so an edited
mapping on either integration shows at the next look. Which library holds a file is still decided by
service paths alone, from the folders the sync has just read.

**No guess, and each unknown says why.** A category whose folder qBittorrent doesn't say, or whose
folder the client's pairs give no app path (`NotCovered`, `Ambiguous`, not a full path), is
`PathUnknown`, with the mapper's sentence when the pairs are why. With no library folder stored yet
the answer is `LibrariesUnknown`, never "outside every library", which would be a claim about
libraries the app has never read; when folders are stored but none of them maps into the app, its
sentence sends the owner to the media library's mappings rather than to another sync. Every status
but `InLibrary` keeps automatic replacement off for the destination (Phase C). Nothing of it is
stored: every read of the destinations asks the client and maps again.

**A Windows host's network folder is read as qBittorrent lists it.** From 4.5 qBittorrent keeps a
category's save path as its own `Path`, cleaned by Qt's `QDir::cleanPath` and listed with `/`
(`Path::data()`), and that cleaning keeps a leading `//` only on Windows, where it starts a UNC
folder. So a save path that starts with exactly two `/` is that folder, Windows-style:
`//nas/media/Movies` is `\\nas\media\Movies`, which a pair such as `\\nas\media` → `/media` maps, and
a 5.2 subcategory without a save path of its own is `\\nas\media\Movies\4K`. Read by its shape alone,
the text would be the Linux `/nas/media/Movies`: the wrong folder, in the wrong style for the owner's
pairs. 4.4 lists a save path as it was saved, uncleaned, where a leading `//` may be either, so
there it is `PathUnknown`, and so are `///…`, a server without a share, and every category under a
default save path that starts with `//` (4.4 turns a Windows host's backslashes into `/` there, so it
may be either too, and 4.5 and later never send one: a Windows host's comes with backslashes). The
mirror case holds as well: from 4.5 no Windows host lists a backslash in a save path, so
`\\nas\media\Movies` or `D:\Torrents\Movies` comes from another host, where it is a relative folder
under the default save path whose name holds the backslashes — never the Windows folder its shape
suggests — and under a Windows default save path the two answers contradict each other and the folder
is unknown
(`QBittorrentCategoryPathsTests.FolderOf_a_save_path_with_a_backslash_from_4_5_is_a_relative_folder_of_a_posix_host`,
`QBittorrentCategoryPathsTests.FolderOf_a_save_path_with_a_backslash_from_4_5_under_a_windows_default_is_unknown`).
This is read from qBittorrent's (4.4.5, 4.5.0) and Qt's source; no Windows qBittorrent was asked.

`DestinationPathCheckTests.Check_of_the_adr_0003_samples_puts_every_qbittorrent_category_in_its_jellyfin_library`
and, end to end through a real sync of `virtual-folders.json` and `categories.json`,
`DestinationsListHttpTests.List_checks_every_sample_category_into_the_sample_library_that_holds_it`
pin the rule promised in the Decision above.
`DestinationPathCheckTests.Check_of_the_parent_of_library_folders_is_outside_libraries`,
`DestinationPathCheckTests.Check_compares_posix_paths_with_their_letter_case`,
`DestinationPathCheckTests.Check_without_library_folders_is_libraries_unknown_with_both_paths`,
`DestinationsListHttpTests.List_of_a_category_no_path_mapping_covers_is_path_unknown_with_the_mappings_reason`
and `DestinationsListHttpTests.List_while_no_stored_library_folder_maps_into_the_app_says_to_check_the_mappings_not_to_sync`
pin the edges, and `LibraryLocationFoldersHttpTests.Resync_replaces_every_librarys_folders_with_those_the_server_lists_now`
the stored folders.
`QBittorrentCategoryPathsTests.FolderOf_a_save_path_with_a_leading_double_slash_from_4_5_is_a_windows_hosts_unc_folder`,
`QBittorrentCategoryPathsTests.FolderOf_a_save_path_with_a_leading_double_slash_on_4_4_is_unknown`,
`QBittorrentCategoryPathsTests.FolderOf_a_save_path_with_a_leading_double_slash_that_names_no_unc_folder_is_unknown`,
`QBittorrentCategoryPathsTests.FolderOf_a_category_under_a_default_save_path_with_a_leading_double_slash_is_unknown`
and `QBittorrentAdapterDestinationsTests.ListDestinations_lists_a_windows_hosts_unc_category_and_its_subcategory_inside_that_share`
pin the network folders.
