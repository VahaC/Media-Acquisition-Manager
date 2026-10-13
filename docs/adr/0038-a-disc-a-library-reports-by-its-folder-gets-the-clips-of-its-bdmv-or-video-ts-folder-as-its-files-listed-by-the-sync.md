# 38. A disc a library reports by its folder gets the clips of its BDMV or VIDEO_TS folder as its files, listed by the sync

Status: accepted (V0.3.0)

## Context

Jellyfin takes a folder that holds `BDMV`, or `VIDEO_TS` with a `.vob` in it, for one disc
(`BaseVideoResolver.ResolveVideo`, `IsBluRayDirectory`, `IsDvdDirectory`). It lists the disc as one film
whose path, and its media source's path, is that folder, with `VideoType` `BluRay` or `Dvd`. It names
no file of the disc anywhere.

ADR 0006 makes a file row one name of a file, so the Jellyfin adapter gave such a version no file
(`FileOf`). On the reference installation that left four films with no file — Dirty Dozen and the three
Matrix films, Blu-ray remuxes in Фільми. The folder scan (ADR 0021) found each disc's
`BDMV/STREAM/00000.m2ts`, matched it to no library name and counted it as a file in no library. So on
2026-10-13 the **Files in no library** dialog (MAM-42) said of each "In Фільми's folders, but Фільми
didn't list it", which is false, and their 81 GiB counted as in no library rather than as Фільми's. The
Library page showed the four versions with no file, so no resolution, tracks or size. The Duplicates
page offered each clip as a possible match by its folder's name, not as a copy of its film.

## Decision

**When a library reports a version by a disc folder, the sync lists that folder's media files inside
its own `BDMV` or `VIDEO_TS` folder and stores them as the version's files, in the library's terms,
before the snapshot is written (`LibraryDiscClips`).** The adapter keeps the folder as the version's
`DiscFolderPath`, only for a local, not remote disc with a path the app can keep. The sync maps the
folder into the app through the integration's own mappings (ADR 0003). It walks the folder with the
allowed roots' checks (`IFileInventory.ListMediaFilesUnderAsync`): inside the roots by its text, and still
inside once every link on the way is followed. Each clip is named as the library's folder followed by the
clip's place inside it.

**Every reader of a library's files counts them unchanged.** A clip is a name with the library's
provenance, its version and its library location (matched by its path as any reported file). So:
- the folder scan takes it for reported (`LibraryAppPaths`) and stores no name for it;
- the Jellyfin row on the Library page counts its room;
- the inspection reads it through the integration's mappings;
- the Duplicates page groups it under its film;
- replacement and removal already stop for a disc's clip (`DiscFolders`, ADR 0030).

Nothing needed a rule of its own, and no column or migration was added.

**Only the disc's own folders.** A file counts as a clip only if the first folder below the disc folder
is `BDMV` or `VIDEO_TS`, letter case ignored, where Jellyfin looks. A sample or bonus video beside those
folders is not the disc's, and the scan still lists it in no library. A link inside the disc that leads
out of it is refused, even into another media folder, because the disc folder is the walk's only root.

**A disc the app can't reach costs nothing but its files.** In these cases the version gets no file, as
before:
- the folder doesn't map;
- it lies outside the media folders;
- it is a link leading out;
- it is away.

The sync still succeeds, because the library itself was read whole (ADR 0008). With no media folder
allowed, nothing is walked. Two versions naming one folder list it once.

| Option | Why not |
|---|---|
| The folder scan ties each clip it finds to the version whose disc folder holds it (a `DiscFolderPath` column, scan names with a version) | A library's name without the library's provenance. Every reader that tells a library's names from the scan's would need an exception: the per-library counts, the snapshot store's replacement of its own rows, the purge of a removed integration, the unlinking when a version goes. A migration would carry the folder. |
| Take the clip's name from Jellyfin | Jellyfin names none: its media source's path is the folder, and its streams carry no file name. |
| Store the folder itself as the version's file | ADR 0006: a row is one name of a file. A folder has no identity or size the app could read, and the scan would still find the clip. |

`LibraryDiscClipsHttpTests.Sync_stores_the_clips_of_a_blu_ray_folder_as_its_versions_files_in_its_library`,
`LibraryDiscClipsHttpTests.Scan_after_the_sync_lists_no_clip_of_the_disc_in_no_library_and_the_jellyfin_row_counts_their_room`,
`LibraryDiscClipsHttpTests.Sync_gives_a_disc_outside_the_media_folders_no_file_and_succeeds`,
`LibraryDiscClipsHttpTests.Sync_names_the_clips_in_jellyfins_terms_when_its_paths_map_into_the_app`,
`LocalFileInventoryFolderListingTests.ListUnder_refuses_a_folder_that_is_a_link_leading_out_of_the_roots`
and `JellyfinAdapterItemsTests.ListItems_keeps_the_folder_of_a_local_blu_ray_or_dvd_as_its_disc_folder`
pin it.

## Consequences

A sync now reads the disk for discs. Before, it only asked the library. It reads only the disc folders,
which are a handful on the reference installation, never the whole media folders, and nothing is opened
for reading. A disc's clip has no size as reported, since the library reported none, so it shows the
size the app read. The Library page's sort by size, which adds up the sizes the libraries reported,
still counts a disc as 0, as before. A disc reached only after the sync (a mount back, a mapping fixed)
gets its files at the next sync, not at the next scan: until then the scan lists its clips in no library,
as it did before this ADR.

## Anti-consequence

This doesn't let a file name decide what a library item is (ADR 0017). Which film the disc is stays the
library's word. The sync only names the files of a version the library itself reported, in a folder the
library itself gave.
