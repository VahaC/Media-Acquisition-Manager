# 39. A download client without categories gets the library folders it can reach and its own default folder as destinations

Status: accepted (0.4.0, MAM-29)

## Context

[ADR 0010](0010-a-destination-is-a-download-clients-own-category-never-a-folder.md) made a
destination "a download client's own category, never a folder of the app's". That fits qBittorrent:
each category saves into a folder the owner set there. Phase D puts Transmission in place of
qBittorrent (Vasyl's decision of 2026-10-13), and Transmission has no categories. It has labels,
which say nothing about where a torrent lands, and one default download folder (`download-dir` in
`session-get`).

Under ADR 0010 as written, a Transmission destination could only be a label. Every torrent would land
in the default folder, and `DestinationPathCheck` would answer `PathUnknown` for every destination.
Every status but `InLibrary` keeps automatic replacement off, as specification §3.2 asks, so Phase
D's acceptance bar, "and (for a client) replace", couldn't be met.

ADR 0010's reasons against a folder are about a client that has categories: a stored folder goes
stale when the owner changes the category, a folder sent as `savepath` overrides the category, and a
folder the owner types is a second setting that can disagree with the client. Transmission has no
category to go stale, to override or to disagree with.

## Decision

**For a download client without categories, the destinations are each Jellyfin library's root
folder, mapped back into the client's paths through the client integration's path mappings and
confirmed by the client's free-space answer, plus the client's own default download folder, marked
outside the libraries with replacement off; a send is one `torrent-add` with that folder as
`download-dir` and the destination's name as its one label. A client with categories (qBittorrent)
keeps ADR 0010 as it is.**

**The libraries say where media belongs.** The folders come from the library root folders the sync
already stores (the MAM-11 section of
[ADR 0003](0003-service-paths-reach-the-app-through-an-explicit-prefix-mapping.md)). Each root is
mapped into the app through its own Jellyfin integration's pairs, as today, then back through the
Transmission integration's pairs. `PathMapper` maps a service path into the app only, so the way back
is new. It keeps ADR 0003's rule: a folder no pair covers, or one two pairs map back differently, has
no folder in the client. That is a status with a sentence, never a guess.

**The client confirms the folder.** Each folder is asked with `free-space` when the destinations are
listed. A folder the client can't see stays in the list, with a sentence saying how to fix it: a path
mapping on the Transmission integration, or the folder mounted into Transmission. Hiding it would
leave the owner wondering where a library went.

**The default folder is a destination too**, for downloads that need no replacement, marked "outside
the libraries, replacement off".

**One write per send.** One `torrent-add` carries the release, `download-dir`, `labels` and
`paused: false`. Transmission 4.0 takes `labels` in `torrent-add`, so a send stays one write
([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).
A film sent to Movies shows in Transmission in, say, `/downloads/movies` with the label "Movies".

**The label is the destination's name**, for the owner in Transmission's web UI. The app never reads
a label back to decide where a torrent is. A label can't hold a comma (Transmission's RPC
specification), so a destination name with a comma is refused for Transmission, with a sentence.

| Rejected option | Why not |
| --- | --- |
| Labels only, no folder | Every torrent lands in the default folder. The folder is unknown (`PathUnknown`), so replacement is off for every destination, and Phase D's acceptance bar fails |
| Radarr's way: the default folder plus a sub-folder key per destination | A new setting the owner must keep in step with the libraries, where ADR 0005 expects the settings page to need nothing new |
| Only the folders Transmission itself reports | It reports one, its default folder. A fresh install would have no library destination at all |
| The owner types the folder | ADR 0010's own rejection: a second setting that can disagree with the client, when the libraries already say where media belongs |

No code implements this yet. MAM-30 builds it, and its tests will be named here.

## Consequences

**What MAM-29's samples confirm** (Transmission 4.1.3, and 4.0.6 where said, 2026-10-13):
- `session-get` reports the default folder, on both versions.
- Both versions take `labels` in `torrent-add`. A label with a space came back as sent.
- A multi-file torrent lands in a folder named after it, under `download-dir`.
- `free-space` answers HTTP 200 either way: `size-bytes` for a folder that exists; `result` "No such
  file or directory" and `size-bytes` -1 for a missing one. A folder Transmission can't see
  (`/downloads`, not mounted) answers exactly like a missing one, so one sentence must fit both.

**What changes in the code (MAM-30):**
- A way back from an app path to a service path, beside `PathMapper`.
- A Transmission destination needs its folder at the send. `DestinationMappingEntity` keeps only the
  client's own name today (ADR 0010's "no folder anywhere"); MAM-30 decides how the row holds the
  folder. Where the destination saves is still checked each time it is shown.
- One `free-space` read per folder when the destinations are listed, and the comma check.

**What MAM-30 must still verify:** the comma rule, which wasn't recorded; a torrent of one file; a
duplicate `torrent-add` with another folder or label (MAM-29's changed nothing seen, with the same
ones); the incomplete folder, which the linuxserver image ships on and no torrent field names, so
MAM-30 reads `incomplete-dir-enabled` and says how to turn it off; and a default folder that is also
a library folder, listed once.

## Anti-consequence

This doesn't let the owner type a folder, and it gives qBittorrent no folder: a client with categories
still sends its category alone, with automatic management (ADR 0010's MAM-13 section). A folder that
`free-space` confirms is where to send, not proof of where a file is. Phase C still checks each file by
its identity (ADR 0003). And it isn't built for two clients side by side: Phase D's case is
Transmission in place of qBittorrent.

## Implementation (added 0.4.0, MAM-30)

**Built as decided: a client says what its destinations are (`DownloadClientCapabilities.DestinationKind`,
`Category` or `Folder`), and for a `Folder` client — Transmission — the app proposes the library folders
mapped back into the client's paths, the client lists its own `download-dir` and each proposed folder,
each confirmed by one `free-space`, and a destination keeps the folder as the client names it (Vasyl's
decisions of 2026-10-13).** The Decision's "No code implements this yet" no longer holds, and the
tests are named below. qBittorrent is a `Category` client and ignores the proposed folders; nothing of
ADR 0010 changed for it.

**How the row holds the folder.** In `DestinationMappingEntity.ClientDestinationKey`, the column a
category's name lives in (256 characters at most), as the client names the folder, in `NormalizedPath`'s
canonical text; no column was added. A folder key is compared as a path (`NormalizedPath.IsSameAs`: any
letter case for a Windows folder, exact for a POSIX one), a category's name ordinally, as before. Each
job keeps its client's kind (`DownloadJobs.DestinationKind`, migration `AddDownloadJobDestinationKind`,
`null` for a job sent before, read as a category), so its folder is shown as a folder even once the
client is gone.

**The way back.** `PathMapper.MapBack(appPath, pairs)` mirrors `Map` step for step: every pair whose app
prefix covers the path is applied, results that agree (`IsSameAs`) are one, none is `NotCovered`, two or
more are `Ambiguous` and named, never the longest prefix; a folder name holding `\` under a Windows
service prefix is `Invalid`; it never throws. Mapped back, then forward again, a folder always lands on
the library folder it came from (`PathMapperBackTests`, the round trip over every pair set
`PathMappingRules` accepts among them).

**The list.** The checker reads the library folders first, maps each back, and proposes the distinct
results; the client answers its default folder and each proposed one, merged by `IsSameAs`, with
`Problem` set where `free-space` failed or the default doesn't parse. Each entry is then checked forward
as before (`DestinationPathCheck`), so a library folder is `InLibrary` with its libraries' names, and the
default folder outside them is `OutsideLibraries` or `PathUnknown`, replacement off either way. An entry
with a `Problem` is `FolderNotSeen` — listed with the client's sentence, its libraries named, no path in
the app, not pickable, and a send to it refused. A library folder with no folder in the client — no pair
leads back to it, two lead back from different places, Jellyfin's own pairs can't place it, or its
folder is longer than 256 characters — is listed apart as unplaced, with the integration, the library and
the fix. Jellyfin's Collections and playlists libraries (`CollectionType` `boxsets`, `playlists`) hold no
media files of their own: the sync keeps the library and stores no folder for it
(`LibraryLocation.HoldsMediaFiles`), so neither is ever proposed.

**Save and every look.** Save takes a new or changed folder only when it is among the live candidates
and confirmed — the request's own key is never proposed, so a folder can't be typed — and stores the
listed entry's canonical key; an update that keeps the client and the folder asks nobody (decision 3 of
MAM-11). Every later look proposes the stored keys concerned too, so a stored folder is checked as
itself: one that doesn't parse, or that the client no longer lists or can't see, is `FolderNotSeen` for
that row alone, never the whole client failing. The Edit dialog's list proposes the destination's own
folder, so a mapping edit never shows it as missing: the dialog asks
`GET /api/settings/destinations/client-destinations/{clientId}?destinationId={id}`, and a folder
client's list then proposes that destination's stored key when it parses — one that doesn't is listed
with why and never proposed; a destination of another client proposes nothing more, and a malformed
id is 400, nobody asked.

**What MAM-29 left to verify, answered.**
- *The comma rule.* Transmission refuses a label with a comma — "labels cannot contain comma (,)
  character", and an empty one, "labels cannot be empty" —, and trims the rest (`makeLabels`, 4.0.6 and
  4.1.3 `rpcimpl.cc`). A name with a comma is refused for a `Folder` client before it is saved, on
  create and every update, a rename included (`DestinationRules.ValidateLabel`;
  `DestinationRulesTests.ValidateLabel_refuses_a_name_with_a_comma`). Names were never blank.
- *A torrent of one file* lands straight in `download-dir`, with no folder (`torrent-metainfo.cc`'s
  `finish()`); a torrent of several lands in a folder named after it, as recorded. No setting changes
  this, so the overwrite check reads that one layout (ADR 0034's MAM-30 section;
  `TorrentTargetPathsTests.For_own_layout_only_of_one_file_is_its_name_in_the_folder_alone`).
- *A duplicate `torrent-add` with another folder or label* changes nothing: `tr_torrentNew` finds the hash
  before it reads the add's folder, labels or `paused`. The sender's look for the release's hashes,
  made before every send, refuses such a torrent first ("“Transmission” already has this torrent, so
  nothing was sent."); one Transmission takes between that look and the add fails the job, naming
  where that torrent lies and its labels, read with one `torrent-get` (ADR 0013's MAM-30 section).
- *The incomplete folder.* `incomplete-dir-enabled` on refuses every call, with how to turn it off. A
  torrent that started there moves into its folder only once it is done, which no answer shows, so the
  README says to turn it off before anything is sent and never on while downloads run.
- *A default folder that is also a library folder* is listed once, as that library's, marked as the
  client's folder too (`isClientDefaultFolder`). One inside a library folder but not its root keeps its
  honest forward status, `InLibrary`, and so can take a replacement: an edge the Decision's "marked
  outside the libraries" didn't foresee. One outside every library is `OutsideLibraries` or
  `PathUnknown` and takes none.

Each item is pinned: the comma by `DestinationsFolderSaveHttpTests.Create_with_a_comma_in_the_name_is_400_and_asks_no_client`,
`Update_renaming_alone_to_a_name_with_a_comma_is_400_and_asks_no_client` and
`Update_moving_a_destination_whose_name_has_a_comma_to_a_folder_client_is_400`; the torrent of one
file by `TorrentTargetPathsTests.For_own_layout_only_of_one_file_is_its_name_in_the_folder_alone` and
`DownloadsSendOverwriteFolderClientHttpTests.Create_looks_only_where_the_client_writes_a_torrent_of_one_file`;
the duplicate by `TransmissionAdapterSubmitTests.Submit_of_a_torrent_transmission_already_has_is_refused_with_where_it_is_and_changes_nothing`
and `Submit_of_a_duplicate_whose_read_after_fails_is_still_refused_in_fewer_words`; the incomplete
folder by `TransmissionAdapterTests.TestConnection_with_the_temporary_folder_on_is_refused_with_how_to_turn_it_off`,
`TransmissionAdapterDestinationsTests.ListDestinations_with_the_temporary_folder_on_is_refused_with_how_to_turn_it_off`
and `IntegrationsRealAdaptersHttpTests.Test_of_transmission_with_its_temporary_folder_on_says_how_to_turn_it_off`;
the default folder by `DestinationsFolderClientHttpTests.ClientDestinations_lists_a_download_folder_that_is_a_library_folder_once_as_that_librarys_row`,
`ClientDestinations_shows_a_download_folder_inside_a_library_folder_in_that_library` and
`ClientDestinations_names_on_the_wire_that_an_entry_is_the_clients_own_download_folder` (the raw
`"isClientDefaultFolder":true`), and
`TransmissionAdapterDestinationsTests.ListDestinations_lists_a_proposed_folder_that_is_transmissions_own_once_as_its_download_folder`.

`PathMapperBackTests.MapBack_of_the_adr_0003_samples_puts_every_jellyfin_library_into_the_client_folder_of_its_category`,
`PathMapperBackTests.MapBack_then_Map_lands_on_the_same_app_folder_for_every_set_of_pairs_the_save_rules_accept`,
`DestinationPathCheckTests.Check_of_a_folder_the_client_can_not_see_is_folder_not_seen_naming_its_library_with_no_app_path`,
`DestinationPathCheckTests.Check_by_key_of_a_folder_the_client_did_not_list_is_folder_not_seen_for_that_row_naming_its_library`,
`DestinationPathCheckTests.Check_by_key_of_a_folder_that_is_not_a_full_path_is_folder_not_seen_never_a_missing_category`,
`DestinationPathCheckTests.Check_by_key_of_a_folder_finds_the_same_folder_however_it_is_spelt`
and `DestinationsClientDestinationsHttpTests.ClientDestinations_names_on_the_wire_each_field_the_dialog_reads_for_a_category_client`
pin the contract and the checks; the folder client's flow is pinned over HTTP against a fake folder
client the app factory adds (`FolderClientApiFactory`, `FakeFolderDownloadClient`):
`DestinationsFolderClientHttpTests`, `DestinationsFolderSaveHttpTests`,
`DownloadsSendFolderClientHttpTests`, `DownloadsSendOverwriteFolderClientHttpTests`,
`DownloadJobTrackingFolderClientHttpTests` and `ReplacementScheduleSendFolderClientHttpTests`; and
Transmission's side of the contract by `TransmissionAdapterDestinationsTests` and
`TransmissionDownloadClientContractTests` against `FakeTransmissionServer`, and end to end over HTTP
through the real adapter by `DestinationsTransmissionHttpTests`, `DownloadsSendTransmissionHttpTests`
and `ReplacementTransmissionFlowHttpTests`.
