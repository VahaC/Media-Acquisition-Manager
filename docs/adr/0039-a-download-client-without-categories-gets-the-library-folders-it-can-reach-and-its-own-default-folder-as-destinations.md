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
