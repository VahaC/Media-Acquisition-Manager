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
