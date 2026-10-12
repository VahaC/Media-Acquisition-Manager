# 29. A possible match is confirmed or dismissed by the owner alone, kept by its group's key, and never authorises removal

Status: accepted (0.2.1, MAM-21)

## Context

Specification §3.3 says a possible match — title, year, duration and other clues where ids are
missing — must "require manual confirmation" and is never used for removal. MAM-20 works the groups
out on every read and stores nothing
([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md));
its Anti-consequence left confirming, dismissing and their audit rows to the Duplicates page
(MAM-21). The ticket asks for both: a decision on a possible match, stored with an audit row, a
confirmed one shown as confirmed and never authorising removal, and no delete, move or bulk action
of any kind.

Vasyl's decisions of 2026-10-07: only the installation owner confirms or dismisses, members see the
decision with the buttons disabled and the reason (the MAM-34 pattern); a dismissed match leaves the
list, every tab included, and a **Dismissed** tab lists the dismissed ones, where the decision can be
undone; one file's names get a tab of their own; and the page is reached from the header, for every
signed-in user, and from a button on the Library page. Stated with no objection: a decision is kept
by the group's key, a group whose members change asks again, only possible matches are decided, a
confirmation never makes the works one or changes `needsReview`, and an undo writes an audit row.

The obvious places to keep a decision go wrong. A group has no row to hang it on (ADR 0028). Rows per
work or per pair of works outlive what the owner saw: confirm two Arrivals of 2016, let a third
library report a third, and a confirmation by work ids, or by pairs, would cover a group of three no
one has looked at — or half of it. A confirmation that joined the works, or cleared the review flag,
would turn a title and a year into the reliable evidence §3.3 asks of a same-work group, which ADR
0012's Anti-consequence refuses to a title match. And the other classes need no one's word: one
device and inode, one SHA-256 or one registry key is the evidence, and a same-work group's flags are
for the owner to read, not to wave away.

## Decision

**The owner's Confirm, Dismiss and Undo of a possible match are kept as one row per group key — the
key ADR 0028 gives the group on every read — with an audit row for each change; a decision is a note
on the list and nothing more: it never joins works, never changes `needsReview`, never links, moves
or removes anything, and a group whose members change has another key and is asked about again
(Vasyl's decisions of 2026-10-07).**

**Kept by the key.** `DuplicateMatchDecisions` holds `GroupKey` (32 characters, a unique index: one
decision per group, which also settles two first decisions racing), `Decision` by name (`Confirmed`
or `Dismissed`, `DuplicateMatchDecision`), who decided last — `DecidedByUserId`, set to `null` when
that user is deleted, and `DecidedByUserName`, copied as an audit row copies it —, `DecidedUtc`, when
it was last given, and `CreatedUtc`, when the group was first decided. A possible match's key is the
hash of `Possible|` and its sorted subjects — its works and the names no library reports —, so a
decision names exactly the members the owner saw. There is no foreign key into works or files:
the groups are stored nowhere, and a row whose key no group has now stays, matching nothing, and
comes back into force if the same members do.

**Only the group a key names now, and only a possible match.** `PUT` and `DELETE
/api/library/duplicates/{key}/decision` work the group out as the list does
(`ILibraryDuplicateService.FindAsync`, asking no file system: it decides an estimate, never a key).
The decision is checked first, by name in any letter case and never by number ("A decision is
needed: Confirmed or Dismissed.", "“1” is not a decision. Use Confirmed or Dismissed."; a body that
isn't one: "The body must be JSON with a decision, such as { "decision": "Confirmed" }."); then a key
that isn't 32 lowercase hex characters is a bare 404 without reading the library, as is one no group
has now; a group of another class is 400 "Only a possible match is confirmed or dismissed: this group
is one by its files' identity, bytes or registry ids, which no one has to confirm." The route takes
the key unconstrained, so another shape is the same 404 as an unknown key, not a missing route.

**The owner's alone (Vasyl's decision 1).** Both actions carry `[RequireInstallationOwner]`: they
live beside the list in `Controllers/Library`, outside `Controllers/Settings`, so each declares its
gate, and `LibraryWritesAreOwnerOnlyHttpTests` discovers both routes, sends them as a member — 403
"Only the installation owner can do this." — and checks that no decision is left behind. The list
stays every signed-in user's. Each group carries `decision`, last, `null` while undecided, else
`{ decision, decidedUtc, decidedByUserName }`; the name is the owner's sign-in name, half of the
owner's credentials, so it goes to the owner alone, as a link's setter's does (ADR 0026).

**Confirmed stays, dismissed leaves (Vasyl's decision 2).** Without `decision=` the list leaves the
dismissed groups out, on every class's tab and in the totals; `decision=Dismissed` lists them alone,
`decision=Confirmed` the confirmed ones, either with `class=`. A confirmed match stays in its place,
still `Possible` with `needsReview` true; the page shows **Confirmed** where it showed **Needs
review**. The **Dismissed** tab offers **Undo** on each card. On the page a dismissal leaves a short
dashed row in its card's place — "“Arrival (2016)” is dismissed: it is listed under Dismissed now."
— with **Undo**, which takes the keyboard focus, as **Not interested** leaves a recommendation; an
**Undo** on **Dismissed** leaves "“Arrival (2016)” is back among the possible matches." with
**Dismiss again**. Either way back puts the card where it was, and the rows stay until the tab or
the page changes, so the list never closes up under the pointer with nothing at hand to undo it.

**Every change has its audit row, a repeat has none.** `AuditAction` gains `DuplicateMatchConfirmed`
(24), `DuplicateMatchDismissed` (25) and `DuplicateMatchDecisionUndone` (26), on a target of the new
kind `DuplicateMatch` (6) whose id is the key read as a Guid — 32 hex digits whose `N` form gives the
key back, so every decision about one group has one target, whichever row held it — and whose name
is the first title and year the group was offered by, "Arrival (2016)", cut to 200 characters. A
change of mind updates the row and records the decision it changed to. The same decision again, and
an undo with nothing to undo, answer 204 and write nothing, so a double click or a retry records
nothing twice. An undo deletes the row and records its audit row in one transaction. Two first
decisions racing on one key: the unique index lets one commit, and the other, on a
`DbUpdateException` with the row now there, is given once more to that row as a decision made just
after it, logged at Information; anything else stays a failure (the `RecommendationDismissalService`
pattern).

| Rejected option | Why not |
| --- | --- |
| A decision per work, or per pair of works | A third work joining the group would inherit a confirmation about two, or half of one |
| Carry a decision over to the key a changed group gets | The owner confirmed the members he saw, not the new one |
| Delete a row once no group has its key | A library switched off for a day would make the owner decide again; the rows are few and small |
| Confirming joins the works, or clears `needsReview` | A title and a year become the id evidence §3.3 asks of a same-work group (ADR 0012's Anti-consequence) |
| Decisions on every class | One identity, one hash or one registry key needs no one's word; a same-work group's flags are the owner's to read |
| Every user their own decision, or members decide | Vasyl's decision 1: the household's one list, the owner's word |
| A dismissed match kept on the list, greyed out | Vasyl's decision 2: it leaves, and the Dismissed tab brings it back |
| An `Undecided` value stored on Undo | An undecided group has no row; what was decided and undone is the audit trail's |
| The decision bound as an enum | `1` would be read as `Confirmed`, and a bad value would get a validation body, not the sentence |
| 409 for the same decision again | A double click or a retry is no error; 204 with nothing written |

`LibraryDuplicateDecisionsHttpTests.Decide_confirmed_is_204_and_stores_the_decision_with_an_audit_row`,
`LibraryDuplicateDecisionsHttpTests.List_shows_a_confirmed_match_as_confirmed_still_needing_review_with_the_deciders_name_for_the_owner_only`,
`LibraryDuplicateDecisionsHttpTests.Decide_dismissed_takes_the_group_off_every_list_but_the_dismissed_one`,
`LibraryDuplicateDecisionsHttpTests.Decide_the_same_decision_again_is_204_and_writes_nothing`,
`LibraryDuplicateDecisionsHttpTests.Decide_another_decision_changes_the_row_and_writes_a_second_audit_row`
and `LibraryDuplicateDecisionsHttpTests.Decide_when_another_request_decides_first_is_204_with_one_row_and_the_last_decision`
pin the decision and its audit rows;
`LibraryDuplicateDecisionsHttpTests.Decide_as_a_member_is_403_with_the_owner_sentence_and_writes_nothing`,
`LibraryDuplicateDecisionsHttpTests.Undo_as_a_member_is_403_with_the_owner_sentence_and_keeps_the_decision`
and `LibraryDuplicateDecisionsHttpTests.Decide_and_undo_without_a_token_are_401_and_write_nothing`
the gate;
`LibraryDuplicateDecisionsHttpTests.Decide_and_undo_of_a_key_no_group_has_are_bare_404s`,
`LibraryDuplicateDecisionsHttpTests.Decide_and_undo_of_a_same_work_group_are_400_with_the_sentence`,
`LibraryDuplicateDecisionsHttpTests.Decide_with_a_decision_missing_or_not_one_is_400_with_the_sentence_and_writes_nothing`,
`LibraryDuplicateDecisionsHttpTests.Decide_checks_the_decision_before_the_key` and
`LibraryDuplicateDecisionsHttpTests.Decide_with_a_body_that_isnt_a_decision_is_400_with_its_sentence_never_a_validation_body`
the refusals;
`LibraryDuplicateDecisionsHttpTests.Undo_forgets_the_decision_with_an_audit_row_and_the_group_is_back_undecided`
and `LibraryDuplicateDecisionsHttpTests.Undo_without_a_decision_is_204_and_writes_nothing` the undo;
`LibraryDuplicateDecisionsHttpTests.A_decision_does_not_follow_a_group_whose_members_changed` and
`LibraryDuplicateDecisionsHttpTests.A_decision_changes_nothing_in_the_library` the key and the note;
`LibraryDuplicatesHttpTests.List_with_an_unknown_decision_is_400_naming_the_decisions` the filter;
`DuplicateMatchDecisionPersistenceTests` the table, its unique key and a deleted user;
`AuditTrailTests.DuplicateMatch_targets_the_groups_key_as_a_guid_and_cuts_a_long_title_with_an_ellipsis`
the target; and the web app's `DuplicateGroupCard.decisions.test.tsx` the buttons, a member's
disabled ones and a 404 that says the group changed and asks for the list again, and
`DuplicatesPage.decisions.test.tsx` the short rows with their **Undo** and **Dismiss again**, the
focus, a row's failure — a changed group included — and the rows dropped on another tab.

## Consequences

**The owner's word lasts while the group does.** A sync, an inspection or a scan that changes no
member leaves the key, so a decision survives them all, and so does another file or version of a
work already in the group, as a work is one subject however many files it has. A work or a scanned
name that joins or leaves the group gives it another key, and the group is asked about again,
undecided and needing review; the old key's row stays, and its decide and undo answer the bare 404
of a key no group has. A page loaded before the change still holds the old key: on that 404 it asks
for the list again, and the card or its short row says "This group has changed since the page
loaded: the list shows it again as it is now."

**The rows are never pruned.** A row whose members never come back matches nothing for good. The
list reads the whole table on every request — one query, a row per possible match ever decided —
and meets the groups by key in memory; a row only ever meets the possible match it was given for, as
a key carries its class. The decisions are the owner's work and live only in `mam.db`, so they are
in its backup; the migration `AddDuplicateMatchDecisions` only creates the table and its two
indexes.

**Known limits, left on purpose.** `needsReview` stays true on a confirmed match, so a client that
reads only that flag still sees it as needing review; the page reads `decision`. A dismissal is the
household's: a member finds a match the owner dismissed only under **Dismissed**. A member sees when
a match was decided, never by whom. The audit trail still has no screen. A same-work group flagged
for review can't be marked as looked at.

## Anti-consequence

A decision licenses nothing. A confirmed possible match is not a same-work group, no reason to
remove, replace or quarantine a copy, and no input to Phase C's replacement, which still makes the
owner pick the specific old copy and checks it (§3.4). A dismissal hides a group from the
default list and from no one: `decision=Dismissed` lists it to every signed-in user. Nothing is
written to Jellyfin, a file or a download client, and the Library, the Search page and the
recommendations read the works as before. This doesn't license decisions on the other classes, a
bulk Confirm or Dismiss, or a member's decision.

## Keep both: copies kept as they are, by the same decision (added 0.3.0, MAM-40)

**The owner's Keep both — Keep all past two files — on a group of exact copies (`ByteIdentical`) or
of one work's copies (`SameWork`) is this ADR's `Dismissed`, on the same routes, with the same row
and the members it was kept with: the group leaves every tab for "Kept as they are", the Dismissed
tab renamed, and comes back when a new file joins it; a confirmation of copies, and any decision on
one file's names, are refused (Vasyl's decisions of 2026-10-12).** Keep both removes, moves and
allows nothing; removing a copy is [ADR
0037](0037-a-copy-is-removed-from-the-duplicates-page-into-the-quarantine-on-the-owners-word-through-replacements-gates-and-checks.md)'s.

**Why.** On 2026-10-12 Vasyl found production's four groups of one work's copies on the list with no
way to say he had looked at them: the Decision's "Only a possible match is decided" refused them.
His decision: Keep both exists, and the group comes back when a new file of the work appears.

**Which class takes what (`DuplicateDecisionRules.RefusalOf`).** A possible match: `Confirmed` or
`Dismissed`, as before. Exact copies and one work's copies: `Dismissed` alone, and its undo; a
confirmation is 400 "These files are copies by their bytes or registry ids, which no one has to
confirm: keep them as they are, or remove a copy." One file's names: any decision and any undo is
400 "These are names of one file, not copies of it: removing a name frees nothing, so there is
nothing to keep or remove." The Decision's sentence for a group of another class, "Only a possible
match is confirmed or dismissed: …", is gone, and with it
`LibraryDuplicateDecisionsHttpTests.Decide_and_undo_of_a_same_work_group_are_400_with_the_sentence`;
`LibraryDuplicateKeepBothHttpTests` covers both classes instead.

**Kept with its members (`KeptMembers`).** A same-work key holds only its works and a byte-identical
key only its hash, so a new copy joining keeps the key, where a possible match's key changes with its
subjects. `DuplicateMatchDecisions` gains `KeptMembers` (`AddKeptDuplicateMembers`, a nullable text
column, no length limit): for kept copies the group's members by path key — the grouping's own, never
a file id a sync replaces nor an identity a first inspection gives a name —, in ordinal order, one
per line; `null` for a possible match. A decision on kept copies is in force while every member the
group has now is among them (`DuplicateDecisionRules.IsInForce`): a new file joining makes the group
undecided again, on its tab with no decision on its card; a file leaving changes nothing. A row out
of force is ignored, never deleted: it holds again if the group comes back to members it covers;
keeping the group again overwrites it with the members it has then and writes a new audit row; and
an undo with no decision in force answers 204 and writes nothing. A new work joining gives a
same-work group another key, which is asked about again, as the Decision says.

**The list.** `decision=Dismissed` lists kept copies beside dismissed matches, and without
`decision` they leave every tab and the totals, as a dismissed match does. A kept group's `decision`
is `{ decision: "Dismissed", decidedUtc, decidedByUserName }`, the name the owner's only, so members
read that it was kept and when; one file's names always have `null`. The page says "Kept as they
are" where a dismissed match says "Dismissed", "Kept as they are by owner on …. A new copy joining
the group brings it back to the list.", and offers Undo on that tab; a Keep both leaves the short row
"Kept the copies of “Alien (1979)” as they are: the group is listed under Kept as they are now." with
Undo, and an Undo there "“Alien (1979)” is back among the duplicates." with **Keep again**. Keep
both asks no confirmation, as Dismiss asks none.

**Audit.** `DuplicateCopiesKept` (56) and `DuplicateCopiesKeptUndone` (57), on the Decision's
`DuplicateMatch` target, named by the group's first work as its card heads it — "Alien (1979)", an
episode by its series and place, "Clarkson's Farm S02E03", copies no library reports by the first
one's file name — as such a group offers no title and year.

**What of the text above no longer holds.** The title's "a possible match" and the Decision's "Only
the group a key names now, and only a possible match" and its 400 sentence: kept copies are decided
too, as above. The rejected option "Decisions on every class": two classes take `Dismissed` alone,
and only to keep them as they are — no same-work group is confirmed, its flags still the owner's to
read. The Consequences' "A same-work group flagged for review can't be marked as looked at": Keep
both does that. The rest holds: a decision still never joins works, changes `needsReview`, or
links, moves or removes anything.

`LibraryDuplicateKeepBothHttpTests.Keep_copies_of_one_work_is_204_and_lists_them_only_as_kept_with_the_decision_and_an_audit_row`,
`LibraryDuplicateKeepBothHttpTests.Keep_byte_identical_copies_lists_them_as_kept_with_the_room_a_copy_takes`,
`LibraryDuplicateKeepBothHttpTests.Keep_the_same_copies_again_is_204_and_writes_nothing`,
`LibraryDuplicateKeepBothHttpTests.Undo_of_kept_copies_forgets_the_decision_with_an_audit_row_and_they_are_back_undecided`,
`LibraryDuplicateKeepBothHttpTests.A_new_file_of_a_work_in_kept_copies_brings_them_back_undecided_under_the_same_key`,
`LibraryDuplicateKeepBothHttpTests.Keep_again_after_a_new_file_joined_stores_the_members_now_with_another_audit_row`,
`LibraryDuplicateKeepBothHttpTests.Kept_copies_stay_kept_when_a_member_file_leaves`,
`LibraryDuplicateKeepBothHttpTests.A_new_work_joining_kept_copies_gives_them_another_key_and_brings_them_back`,
`LibraryDuplicateKeepBothHttpTests.A_new_copy_joining_kept_byte_identical_copies_brings_them_back_undecided`,
`LibraryDuplicateKeepBothHttpTests.Decide_confirmed_of_copies_is_400_with_the_sentence_and_writes_nothing`,
`LibraryDuplicateKeepBothHttpTests.Decide_and_undo_of_one_files_names_are_400_with_the_sentence_and_write_nothing`,
`LibraryDuplicateKeepBothHttpTests.Keep_and_undo_as_a_member_are_403_with_the_owner_sentence_and_change_nothing`,
`LibraryDuplicateKeepBothHttpTests.Keep_an_episodes_copies_names_the_audit_row_by_its_series_and_place`
and
`DuplicateMatchDecisionPersistenceTests.Kept_members_are_stored_whole_however_many_and_long_and_a_possible_match_has_none`
pin it; `DuplicateGroupCard.decisions.test.tsx` and `DuplicatesPage.decisions.test.tsx` the buttons
and the short rows.
