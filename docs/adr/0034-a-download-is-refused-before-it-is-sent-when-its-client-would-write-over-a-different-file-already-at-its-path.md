# 34. A download is refused before it is sent when its client would write over a different file already at its path

Status: accepted (0.2.1, MAM-38)

## Context

Specification §6 asks Phase A to leave "existing files untouched", and the app writes nothing into
the media folders. But it tells qBittorrent to write there: on the reference installation every
category saves straight into a Jellyfin library folder (ADR 0003, ADR 0010), and the app sends with
the category and automatic torrent management, never a save path. Read from qBittorrent 5.2.4's and
libtorrent 2.0.15's source while MAM-23 was decided (ADR 0032), with no live test:

- **qBittorrent takes over whatever is already there.** Adding a torrent, it keeps the name of every
  file it finds at the torrent's paths and hands them to libtorrent; it refuses only a torrent whose
  info hash it already has. A Toloka update or a re-release has a new info hash.
- **libtorrent writes into it.** A new torrent has no resume data, so any file already at its paths
  starts a full check: the pieces that hash right are kept, the others are downloaded and written
  into the same file, in place. Another release under the same name becomes a mix of old and new
  bytes while it downloads, and stays broken if the swarm stalls; a longer old file keeps its tail
  (POSIX disk I/O) or is cut short (memory-mapped). A file that is another torrent's seeding data —
  822 of 1394 library files on 2026-10-04 (ADR 0018) — is damaged with it.
- **Nothing warns and nothing stops it.** No option of `torrents/add` makes the add safe
  (`skip_checking` only postpones the writes, a stopped add only until it is started), qBittorrent
  has no setting against it, and the app can't stop a torrent once sent (ADR 0014, ADR 0031).
- **The same bytes are fine.** A Toloka update re-adds a series' old episodes byte for byte: they
  pass the check, and only the new episodes download.

The app already holds what a check needs before it sends: the .torrent (Toloka hands out nothing
else), the category's folder from the destination listing it reads anyway (MAM-11's
`DestinationPathCheck`), the Media folders (ADR 0009) and the client's torrents in that category. It
doesn't know qBittorrent's content layout: `app/preferences` carries the Web UI's key and other
passwords, and the app never reads it.

Vasyl's decisions of 2026-10-08: refuse when a file already at a target path differs in size, or has
the same size but a few pieces that don't hash as the torrent says, and send when they do; when the
destination's folder can't be checked, send, with a warning in the Download dialog before Send; send
a magnet marked as not checked; build it in 0.2.1. Stated to the owner without objection: every
content layout's paths count, the torrents qBittorrent already has in the category count, a folder
that is only the same folder passes, and a refusal is a failed job.

## Decision

**Right before the one submission, the sender works out every place each of the .torrent's files may
be written below the destination's folder, under each of qBittorrent's three content layouts, and
refuses the send — a Failed job with the app's sentence naming the first file, nothing handed to the
client — when one of those places holds a file of another size, a file of the same size whose
sampled pieces don't hash as the torrent says, something that isn't a file, a file it can't read, or
a place a torrent the client already has in the category will write; anything else is sent, and the
job says whether it was checked (Vasyl's decisions of 2026-10-08).**

**Where it runs.** In `DownloadSender`, after the client is asked whether it already has the torrent
and before `SubmitAsync`, through `IDownloadOverwriteGuard`. The job is still written before
anything is fetched or sent (ADR 0013): a refusal is an outcome of the job, shown in the dialog as a
failed job's sentence is, the form kept. A check that breaks on something nobody foresaw is logged —
the exception's type and stack, never its message — and ends the job Failed with the app's sentence,
nothing sent: a check that couldn't run can't say the release lands on nothing.

**What it reads.** `TorrentContentReader` reads the file list — v1 `files` or `length`, `name.utf-8`
and `path.utf-8` first, pad files counted but left out (BEP 47), a hybrid by its v1 metadata, a v2
torrent by its file tree. Each name is cleaned as libtorrent 2.0 cleans it on Linux:
`TorrentPathElement` ports `sanitize_append_path_element` (`src/torrent_info.cpp`, RC_2_0). A UTF-8
sequence that isn't valid is one `_` for the whole length its lead byte claims; a control character
is `_`; NUL, `/`, `\` and the bidirectional controls are dropped; DEL and the C1 controls stay. Past
240 bytes a name ends after the character that reaches them, with an extension from a dot among its
last 9 bytes. Every element of a file's path and every key of a file tree is one element — `.`,
`..` and an empty one are `_` —; the torrent's folder is `_` for an empty name and the v1 info hash
for one cleaning drops. A torrent of one file whose name is empty or dropped, or one whose pieces
are longer than libtorrent's 536,854,528 bytes, is a torrent libtorrent refuses, and so a list the
app can't read. `TorrentTargetPaths` gives each file's places: the torrent's own path (Original), a
lone file inside a folder of its name without the extension (Subfolder), a torrent's files without
its folder (NoSubfolder). An empty file is no place: the client creates one only where nothing is.

**Same bytes, cheaply.** A file already there of the torrent's size is compared by up to three
pieces wholly inside it — the first, one in the middle and the last — hashed as the torrent hashes
them, SHA-1 for v1 and a SHA-256 tree for v2 (`IFileInventory.HashPiecesAsync`, read through ADR
0009's checks and facts). Another release differs in every piece, so three suffice, and three pieces
are a few megabytes read, not a whole film. Each piece is hashed a chunk at a time as it is read,
so even a piece of libtorrent's largest size never sits in memory whole. A file with no piece wholly
inside it — a small one between two others in a v1 torrent — is told by its size. A v2 file longer
than a piece whose layer is missing is told by its size too.

**Torrents not on the disk yet.** A place where nothing is yet is compared with the torrents the
client already has in the destination's category (`ListDestinationJobsAsync`). A torrent's files end
up below its save path (qBittorrent's `save_path`), whatever its content path says while an
unfinished download sits in qBittorrent's folder for incomplete torrents. So every torrent is first
compared by what its listing alone tells: its content where it is, or its name in its save path.
Only then are the files of those that may write one of the places read (`ListJobFilesAsync`), and
compared below the save path: a torrent whose own folder or file there has the place's first name,
one with no folder of its own, and one still in the folder for incomplete torrents — at most 20 of
them. Either way a match refuses the send. A torrent still fetching its metadata, files the client
couldn't list, or more than 20 torrents to ask leave the check unable to say.

**What can't be looked at is sent, and said so.** A magnet names no files until the client fetches
them; a .torrent whose list the app can't read; a destination folder without a path in the app (ADR
0003) or outside the Media folders; a place behind a link that leads out of them; a file name that
holds a character the app's paths refuse — DEL or a C1 control, which libtorrent keeps
(`NotCheckedFileName`); a client that can't tell its torrents or their files. Each is sent as
before, and the job's `OverwriteCheck` says why it went unchecked, which the Jobs page shows — but
not on a Failed job, whose own sentence is the whole story. A place that can't be looked at never
hides one that can: the check looks at every place before it answers, and any collision refuses the
send. The Download dialog asks `GET /api/downloads/overwrite-check?destinationId=` when a
destination is picked and warns before Send when its folder can't be checked — every signed-in
user's, as the dialog is, with a reason and never a path, as the Media folders describe the host.
Send waits for that first answer, so the warning is there before anyone can send, but never once
the question has failed, which warns of nothing; Send is described by the warning, so a screen
reader reads it with Send.

**The sentence is the app's.** "“qBittorrent” would write over “Film.2001.mkv” in the folder of
the category “Movies”: a file of another size is already there, so nothing was sent." — the file
by its path below the folder, the reason, and how many more of the release's files land on a file
already there, each file once however many of its places do; everyone who sent the release reads
it. Once one collision refuses the send, a later file of the torrent's size is counted as already
there without reading its pieces: its bytes change nothing about the refusal.

| Rejected option | Why not |
| --- | --- |
| Refuse any file already there | Blocks a Toloka update over the same old episodes, which qBittorrent handles well |
| Compare sizes alone | Another release of the same size (rare, but a re-mux posted under the same name is one) would be written over |
| Hash every file whole | Minutes per film on the reference installation's spinning disk, for what three pieces tell |
| Add the torrent stopped, let qBittorrent check, then start or remove it | A write to the client to stop or remove it, which ADR 0014 and ADR 0031 rule out; no stop condition before qBittorrent 4.6 |
| Send a content layout or a save path that avoids the collision | Overrides the owner's own setting and the category's folder, which §3.2 forbids |
| Read qBittorrent's preferences for its layout | `app/preferences` carries the Web UI's key and other passwords |
| Fetch a magnet's files through qBittorrent first (`torrents/fetchMetadata`) | qBittorrent 5.2 and later only, a new client call, and Toloka hands out no magnets; Vasyl chose to send them marked |
| Refuse a file name the app can't look up | It would refuse a release whose place may well be free; such a name is sent unchecked and said so instead |

## Consequences

**A Toloka update still goes through,** its old episodes compared and its new ones downloaded; a
different release under a name already in the library is stopped before qBittorrent touches it. The
refusal names the file but suggests nothing about it: the library is the owner's (specification §1).

**Two reads of the client more per send** in the common case — the category's torrents, and the
files of the torrents that may write one of the places, 20 at most —, and one question when a
destination is picked in the dialog. No write: the client's one write is still the submission.

**What stays unchecked is visible:** magnets, unreadable file lists, folders outside the Media
folders or without a path, places behind a link leading out of them, names the app can't look up,
and a client that can't tell its torrents or their files. A name libtorrent cleans differently from
`TorrentPathElement` can only make the check look at another place: a collision missed, as before,
or a send refused for a file the client wouldn't have touched.

**Phase C's replacement uses the same check** (ADR 0032): a replacement whose files land on any file
already there is refused, the same-bytes case included, as replacing a file by itself is no
replacement.

`DownloadsSendOverwriteHttpTests` pins it end to end — another size, the same size with other bytes,
a file the app can't read or that changed while it was compared, a lone file in a folder of its
name, something that isn't a file, several collisions and one file colliding under two layouts
counted once, a later file of the same size counted unread, a collision before a link leading out
and beside a name that can't be looked up, a torrent qBittorrent already has (its file, a file in
its folder, one still in the folder for incomplete torrents, and one listed after more than 20
others), the member's sentence, the check after "already has this torrent", a check that throws,
the same bytes sent as checked (v1 with pad files, and v2), a folder that is only the same folder,
each unchecked case — a torrent without metadata, files that can't be listed or throw, more than 20
torrents to ask — and no write to qBittorrent but the sign-in and the one add —;
`DownloadsOverwriteCheckHttpTests` the dialog's question; `TorrentContentReaderTests`,
`TorrentTargetPathsTests`, `TorrentPathElementTests`, `TorrentPieceHashTests`, `BencodeParserTests`,
`DownloadRulesTests` and `LocalFileInventoryPieceHashTests` the parts; `QBittorrentAdapterJobsTests`
the save path; `DownloadDialog.test.tsx` and `JobsPage.test.tsx` the dialog and the Jobs page.

## Anti-consequence

This licenses no write: not to a download client, not to the media folders. The check reads a few
pieces of a file already there and nothing else of it, and it decides only whether this download is
sent — never what happens to the file it would have written over.

## Strict mode for a replacement (added 0.3.0, MAM-24)

**A replacement's send runs this check strictly
(`IDownloadOverwriteGuard.CheckAsync(…, anyFileRefuses: true)`): a file at any place refuses it, the
same bytes included, told by being there and its pieces never read (the collision `AlreadyThere`),
and every way the check couldn't look at a place refuses it instead of sending it unchecked.** The
refusal is this Decision's `Failed` job with nothing handed to the client: "“qBittorrent” would
write over “Alien (1979).mkv” in the folder of the category “Movies”: a file is already there, and a
replacement never lands on a file already there, so nothing was sent.", and for each unchecked case
"The app couldn't check every place this release would write in the folder of the category “Movies”
(<why>), so nothing was sent and no copy will be replaced. Pick Keep both to download it." — a
magnet naming no files, a list of files that can't be read, a folder with no path in the app, a
folder or a link on the way to a place outside the Media folders, a client that couldn't list its
torrents in the category or their files, a name the app can't look up. Before the check, the strict
sender also fails a gateway link that turns out a magnet and a category whose folder isn't inside a
library the app knows. A Keep both send is unchanged: the same bytes go through, and what can't be
looked at is sent and said so. A strict refusal leaves `OverwriteCheck` `null`, as every refusal
does, so the replacement's following sees no `Checked` and fails the replacement (ADR 0014's MAM-24
section).

**The dialog asks first.** The replacement read refuses a destination whose folder this check can't
look into (`FolderReasonAsync`), or that isn't confirmed inside a library, as
`DestinationNotUsable`, so Replace can't be picked there, and in Replace mode **Send**'s reason
takes the place of Keep both's warning; the send checks again, whatever the dialog showed.

**An accepted gap: the category's scope.** A place nothing is at yet is compared, as for every send,
with the torrents the client already has in the destination's own category; a torrent of another
category whose files would land there isn't seen. For a replacement the gap is accepted (MAM-24's
design review): such a torrent writing there later is MAM-25's to catch when it reads the new files,
before anything moves, and closing it here would mean reading every torrent of the client at every
replacement's send.

`ReplacementScheduleSendHttpTests.Schedule_whose_file_would_land_on_the_same_bytes_already_there_fails_and_hands_qbittorrent_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_whose_gateway_link_turns_out_a_magnet_fails_and_asks_qbittorrent_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_to_a_destination_outside_every_library_fails_and_hands_qbittorrent_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_to_a_library_outside_the_media_folders_fails_and_hands_qbittorrent_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_whose_file_lies_behind_a_link_leading_out_of_the_media_folders_fails_and_hands_qbittorrent_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_whose_file_name_holds_a_character_the_app_can_not_look_up_fails_and_hands_qbittorrent_nothing`,
`ReplacementScheduleSendHttpTests.Schedule_of_a_torrent_whose_files_the_app_can_not_read_fails_and_hands_qbittorrent_nothing`
and
`ReplacementScheduleSendHttpTests.Schedule_when_qbittorrent_can_not_list_the_categorys_torrents_fails_and_hands_it_nothing`
pin it, and `DownloadsSendOverwriteHttpTests` holds as it was for Keep both.

## The accepted gap, closed after the download (added 0.3.0, MAM-25)

**The MAM-24 section's accepted gap — a torrent of another category whose files land at a
replacement's place — is closed where it said, when the new files are read and before anything
moves: once every new file passed checks 1 and 2, the check of the new files reads the client's
every torrent with its files, and another torrent listing a new file, by its path or by its device
and inode, stops the replacement for the owner's review ([ADR
0035](0035-a-replacements-new-file-is-checked-by-its-clients-word-the-media-inspector-and-the-librarys-ids-before-anything-moves-and-what-is-unsettled-after-an-hour-stops-for-review.md)).**
"“qBittorrent”'s download “Alien remux” also writes
“/media/movies/Alien.1979.Directors.Cut.2160p.mkv”, so the app can't tell the new file is this
download's own." The read is the whole listing the Download dialog's marks use
(`ListJobsWithFilesAsync`, qBittorrent 5.2 or later), each torrent's files placed in the app the
same way (`ClientFilePlaces`), a hard link at another path found by its device and inode. A torrent
the app can't place — no metadata yet, a save path or a file no mapping places, a file inside the
Media folders whose size or identity can't be read — can't clear the file either, as the Download
dialog's marks count it unplaced: the check waits for it, and stops once its hour runs out ("The app
can't place “qBittorrent”'s download “Alien magnet” in its folders, so it can't tell whether that
download writes the new files."). A client that can't give the listing can't clear the file: one too
old for it stops the job at once ("This version of “qBittorrent” can't list the files of its other
downloads, so the app can't tell whether another one writes the new files."), one out of reach is
asked again until the check's hour runs out. It stays a read. The send is unchanged, and a torrent
added after the check isn't seen by it; MAM-26 reads the old copy's torrents again before the move.

`ReplacementVerificationFilesHttpTests.Verify_of_a_new_file_another_download_also_writes_stops_for_review_without_asking_jellyfin`,
`ReplacementVerificationFilesHttpTests.Verify_of_a_new_file_another_download_reaches_by_a_link_at_another_path_stops_for_review_without_asking_jellyfin`,
`ReplacementVerificationFilesHttpTests.Verify_of_a_new_file_another_download_holds_a_file_of_its_size_of_another_identity_passes_the_files_checks`,
`ReplacementVerificationFilesHttpTests.Verify_where_another_download_cant_be_placed_keeps_checking_until_the_hour_runs_out_then_stops_for_review`
and
`ReplacementVerificationFilesHttpTests.Verify_where_qbittorrent_is_too_old_to_list_its_other_downloads_stops_for_review`
pin it.

## One client per send (added 0.4.0, MAM-29)

**The check reads the one download client a send goes to, and no other.** A check across several
clients — another client's torrent writing to the same place — was considered while Phase D was
decided, and dropped on 2026-10-13 (Vasyl's decision). Phase D's case is Transmission in place of
qBittorrent, not two clients side by side, so this check isn't built for a second client.

For Transmission the check runs before every send, as for qBittorrent. The three content layouts and
libtorrent's name cleaning above are qBittorrent's. Transmission doesn't use libtorrent, so its layout
and its cleaning are MAM-30's to read from Transmission's source. MAM-29's samples showed a
multi-file torrent landing in a folder named after it below `download-dir`; a single-file torrent
wasn't recorded. `rename-partial-files` was on in both versions recorded. By Transmission's
documentation that names an unfinished file with `.part` on the disk; MAM-30 checks it and lets the
check allow for it.

## Transmission's layout, its names and its `.part` files (added 0.4.0, MAM-30)

**For Transmission the check looks at the places its one layout gives, each name read every way
Transmission may clean it, and at each place's `<name>.part` too (`TorrentFilePlacement.OwnLayoutOnly`,
`PartialFileSuffix`).** Read from Transmission's source, 4.0.6 and 4.1.3, as the MAM-29 section planned:

- **One layout.** A torrent of one file lands straight in the folder; a torrent of several, in a folder
  named after it (none if that name cleans to nothing). No setting changes it, so there is no other
  layout to allow for.
- **Three ways to clean a name** (`TorrentNameReadings`), as Transmission doesn't use libtorrent: 4.0 on
  any system cuts at a NUL, splits on `/`, strips the ends' whitespace and trailing dots, puts `_` before
  a Windows device name (`CON`, `con.txt`, `LPT1` …) and turns `" * : < > ? \ |` into `_` (`Portable`);
  4.1 on Windows does that and turns each control character into `_` too (`WindowsSafe`); 4.1 on Linux
  keeps a name as it is but `.` and `..`, which become `_.` and `_..` (`Verbatim`). Each drops what
  ends up empty. The check isn't told the version or the system, so it looks at every place the three
  give, each file once however many of its places it finds.
- **`.part`.** With `rename-partial-files` on, an unfinished file is `<name>.part` on the disk, and
  Transmission adopts what it finds there as it adopts the plain name, so each place is followed by its
  suffixed place, the same file of the release.
- **Adopting is as bad as qBittorrent's.** When every file of a torrent is already there, each of the
  right size and older than the add, Transmission takes the torrent as complete once its first piece
  hashes right; otherwise it checks what is there and writes the bad pieces into those files in place,
  cuts a longer file short, and on 4.1 renames a file that doesn't verify to `.part`. Nothing warns.
  So the check runs before every send to Transmission, as the MAM-29 section said.

Transmission's torrents in the destination's folder are those whose `downloadDir` is that folder (as a
path), read from all its torrents, never by label. A folder Transmission can't see (`FolderNotSeen`) has
no path in the app, so the check can't look into it, and the send refuses it before; the check, asked
about one anyway, refuses it too, in either mode and before it looks at anything, with the send's own
sentence ("“Transmission” can't use the folder of the destination “Movies” any more, so nothing was
sent. The owner checks the destination under Destinations in the user menu.",
`DownloadOverwriteGuardFolderNotSeenTests`). The Download dialog's question answers it with a reason
of its own, `NotCheckedFolderNotSeen`, which no sent job ever carries, as nothing is sent there, Keep
both or a replacement alike, and the dialog holds Send for it in either mode. The sentences name
the destination, never its folder, as members read them: "“Transmission” would write over
“Film.2001.mkv” in the folder of the destination “Movies”: a file of another size is already there, so
nothing was sent." and, for a replacement, "The app couldn't check every place this release would write
in the folder of the destination “Movies” (the client couldn't list its torrents in that folder, or
their files), so nothing was sent and no copy will be replaced. Pick Keep both to download it."
qBittorrent's three layouts and libtorrent's cleaning are unchanged, byte for byte.

**Names as the .torrent holds them, and what can't be predicted.** `TorrentContentReader` keeps the
torrent's name and each file's path elements as the .torrent holds them, beside libtorrent's cleaned
ones, and the readings clean each element on its own. Transmission 4.0 writes a file's path elements
as the bytes they are, so a file path of a torrent of several files held in bytes that aren't UTF-8,
or with a NUL, names a place no path in the app says; such a torrent, like one with a file no reading
gives a name, has places the check can't predict (`TorrentTargetPaths.For` answers `null`): a send
goes unchecked (`NotCheckedFileList`), and a replacement is refused with "The app couldn't check every
place this release would write in the folder of the destination “Movies” (the .torrent's list of files
can't be read), so nothing was sent and no copy will be replaced. Pick Keep both to download it." A
lone file's name is read whatever its bytes, as each reading cleans it. Such an unchecked send is not
what it is with qBittorrent, whose libtorrent refuses a torrent whose list of files the app can't read,
so nothing is written: Transmission adds the torrent and writes its files — 4.0 under the bytes as the
torrent holds them, 4.1 with U+FFFD in place of bytes that aren't UTF-8 — so, as with a magnet, a file
of the same name already there may be taken over or written over. The job says so
(`NotCheckedFileList`), but the Download dialog can't warn before **Send**: the .torrent is fetched
only then. **Pad files.** libtorrent never
writes a file the torrent marks as padding (BEP 47), so qBittorrent's places leave them out.
Transmission ignores the mark (`attr` is "currently unused" in 4.0.6 and 4.1.3) and writes such a file
like any other, so for Transmission each is a place like a real file — but for one at BEP 47's own
name, `.pad/<digits>`, which every reading keeps and which lands in a `.pad` folder no library holds.

**The move from qBittorrent keeps one client per send.** The MAM-29 section's check reads only the
client a send goes to, and the Download dialog offers every switched-on client with a destination
(`DownloadService.GetOptionsAsync`). So the README's and the 0.4.0 release note's steps for the move
have the owner remove qBittorrent's destinations as soon as Transmission's exist, leaving its
integration switched on for the holdings check right before a move (ADR 0031), which reads every
client whatever its destinations: a qBittorrent torrent still queued or fetching its metadata for a
place in a library folder is not on the disk yet, and a send to Transmission would never see it.

`TorrentNameReadingsTests`, `TorrentTargetPathsTests.For_own_layout_only_of_one_file_is_its_name_in_the_folder_alone`,
`TorrentTargetPathsTests.For_own_layout_only_of_a_torrent_with_a_folder_keeps_the_folder`,
`TorrentTargetPathsTests.For_own_layout_only_of_a_read_torrent_places_each_file_every_way_transmission_may_write_it`,
`TorrentTargetPathsTests.For_with_a_partial_file_suffix_adds_each_files_suffixed_place_for_the_same_file`,
`TorrentTargetPathsTests.For_own_layout_only_of_a_read_torrent_whose_path_is_not_utf8_can_not_predict_it_while_qbittorrents_places_stay`,
`TorrentTargetPathsTests.For_own_layout_only_of_a_read_torrent_that_marks_a_media_file_as_padding_still_places_it`,
`TorrentContentReaderTests.Read_keeps_the_name_and_each_files_path_as_the_torrent_holds_them_beside_the_cleaned_ones`
and `TorrentContentReaderTests.Read_marks_a_files_path_whose_bytes_are_not_utf8_as_not_exact_unless_path_utf8_names_it`
pin the parts, and over HTTP
`DownloadsSendOverwriteFolderClientHttpTests.Create_whose_file_would_land_on_its_unfinished_name_already_holding_another_file_fails`,
`Create_whose_file_is_taken_at_its_name_and_its_unfinished_name_counts_that_file_once`,
`Create_looks_only_where_the_client_writes_a_torrent_of_several_files` and
`Create_looks_only_where_the_client_writes_a_torrent_of_one_file`, and
`TransmissionAdapterJobsTests.FindJobs_puts_a_queued_4_0_6_torrents_content_where_transmission_will_write_its_files`
the content's place a 4.0.6 queued torrent reports; `DownloadsSendOverwriteTransmissionHttpTests` goes
end to end through `FakeTransmissionServer`, a queued torrent's place, a `.part` place and a torrent no
reading can place among them.
