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
