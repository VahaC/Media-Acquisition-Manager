# 12. A search or a release matches a library work by identifier, then by title and year, then by title alone, and every match is listed strongest first — never picked, merged or acted on

Status: accepted (0.1.0, MAM-12)

## Context

Specification §3.1 sets the order: "reliable identifier first; then original/localized title plus
year; then a candidate requiring manual review. A title or year match alone never authorizes
replacement", and "Conflicting IDs … prevent automatic matching for removal". ADR 0004 searches
Toloka by text and "matches identifiers on its own side", and its MAM-10 section left that matching
to the search page. ADR 0006 keeps every title and id a source reported as a row of its own, so one
work holds "Інтерстеллар" and "Interstellar", each with the year its source gave, and two sources
can give one work two IMDb ids.

What the sources give makes the obvious shapes go wrong:

- **Toloka lists no ids.** Its listings are titles only, written by whoever uploaded:
  "Інтерстеллар / Interstellar (2014) BDRip 1080p", "Interstellar (2014) BDRemux 1080p Ukr/Eng |
  Sub Eng [IMAX Edition]", and other trackers' scene names, "Interstellar.2014.1080p.BluRay.x264".
  Matching by id alone would match nothing on the reference installation.
- **The best match hides the others.** The same film in two Jellyfin libraries is two works (ADR
  0008 groups per library), and a remake has the same title in another year; showing only the
  strongest hides a copy the person owns.
- **A look-alike is not the film.** A distance between titles takes "Interstellar Wars" for
  "Interstellar" and "Alien" for "Aliens", and a year ± 1 joins a film to its remake's neighbour.
- **A conflict merged is a conflict lost.** Choosing one of two ids two sources gave, or trusting
  the search's id over the work's, removes the disagreement §3.1 wants to block on.

## Decision

**A search, and each release it found, is matched to the library's works by one pure rule
(`WorkMatching`): an identifier both hold, else a title key both hold with that title's own year,
else a title key alone; every work that matches is listed, strongest first, with its evidence, the
id or title that matched and the source that reported it, and every conflict is flagged — nothing
is ever picked, merged, stored or acted on.**

**Against the snapshot MAM-8 imported.** The library side reads the works, titles and ids the last
sync stored, which the Library page shows, never the media server itself: `IMetadataResolver`
stays unimplemented. The search is the claim: its title as the one title, its year, its IMDb and
TMDb ids, each asked in a field of its own with the title required (Vasyl's decision 1: no line
parsed into parts). A release is the claim of its title and its listing (Vasyl's decision 2): the
titles and year its title is read as, and the ids its listing gives. A release is matched against
the works the search found, never against the whole library.

**Identifier.** A scheme and value both hold, the value trimmed and in any letter case (`tt0090605`
is `TT0090605`), the `LibraryWorkGrouping` rule; a blank value names nothing. **Title and year.** A
title of the work — displayed or original, from any source — whose key is a claimed title's key,
and whose own year is the claim's: a source's title and year come as a pair (ADR 0006). **Title
alone.** The key matches and the year doesn't, or one side has none: a candidate to check.

**The title key** (`TitleMatchKey`). The title upper-cased with `ToUpperInvariant`, independent of
the machine's culture; apostrophes dropped, so the letters around them join; every other run of
characters that are not letters, digits or combining marks one space; trimmed. "Spider-Man: No Way
Home" is `SPIDER MAN NO WAY HOME`, "Інтерстеллар" `ІНТЕРСТЕЛЛАР`, "!!!" empty, which matches
nothing. The apostrophe joins rather than splits for two reasons: Ukrainian is typed with three of
them — `'`, `’` and the recommended `ʼ`, which Unicode counts as a letter — so "Дев'ять", "Дев’ять"
and "Девʼять" must be one key (`ДЕВЯТЬ`), and a scene name drops them, so "Ocean's Eleven" must be
`OCEANS ELEVEN` like "Oceans.Eleven.2001". The characters dropped are `'`, `‘`, `’`, `ʼ`, `ʻ`,
`` ` ``, `´` and the full-width `＇`. Nothing else is looked at: no distance, no year ± 1.

**How a release's title is read** (`ReleaseWorkClaims`, read again on every read). The year ends the
title. First the first `(YYYY)` or `[YYYY]` of 1800–2099, in brackets that match, with title text
before it: "Interstellar (2014) BDRemux" is "Interstellar" of 2014, "Taken (2008) Remastered 2019"
still 2008, and a range "(2010-2014)" is a collection's span, not a year. Else a bare year the scene
way: the words split at whitespace, dots and underscores, the first four-digit year from the second
word on, through a run of years — "Interstellar.2014.1080p" is "Interstellar" of 2014, "2012.2009"
is "2012" of 2009, "Blade.Runner.2049.2017" is "Blade Runner 2049" of 2017. The part before the
year splits at a slash with whitespace on both sides into alternatives, so Toloka's "Інтерстеллар /
Interstellar" is both and "Face/Off" stays whole, each trimmed of `-–—:|,.`. No year, no title: in
"Interstellar BDRip 1080p" nothing says where the title ends and the quality begins, so such a
release matches only by an id its listing gives.

**The listing's ids** (the MAM-12 section of ADR 0004). The Torznab attributes `imdbid`, `imdb` and
`tmdbid`, at most one id per scheme; a listing that gives two values of one scheme contradicts
itself and gives none of it.

**Conflicts are flagged, never resolved.** A scheme conflicts when the claim and the work both hold
values of it and share none (the search asked for tt0816692, the work holds tt9999999), or when the
work holds two or more values of it (two sources disagree, ADR 0006). A conflict doesn't stop a
title match: the work still matches by title and year, and says the ids conflict. A work two
sources give two IMDb ids matches either id, with both shown and the conflict flagged.

**Every match, strongest first.** The library side by evidence, then by the title its card shows in
Ukrainian alphabetical order, then by id, never paged: the works one title finds are a handful. A
release lists every work of the search's library side it matches, by evidence and then in that same
order; the page shows the strongest on the release's row and all of them in its Details.

**The database narrows, the rule decides.** One query keeps the works holding an id asked for
(`upper(trim(Value))`; ids are ASCII) or a title whose stored upper-cased copy (`NormalizedText`,
the same per-character `ToUpperInvariant`) holds the longest word of a claimed title's key once the
same apostrophes are taken out of it inside the query (`replace()` within `instr()`). Apart from
those apostrophes a key only changes what lies between words, so every one of its words is in that
copy: the narrowing never leaves out a work the rule would match, and only the rule decides.

| Rejected option | Why not |
| --- | --- |
| Show only the best match | The same film in two libraries, or a remake, hides a copy the person owns |
| A distance between titles, or a year ± 1 | A look-alike becomes a match: "Interstellar Wars", "Aliens" for "Alien" |
| Merge the works a release matches | ADR 0008 joins works by ids only; joining by title and year merges remakes |
| Resolve a conflict by the newest source or the search's id | Hides the disagreement §3.1 blocks on |
| Cut a yearless title at its first quality word | Matches the wrong work as readily as the right one |
| Read one search line into title, year and id | Vasyl chose separate fields |
| Ask the media server through `IMetadataResolver` | A call per search, and an answer that can disagree with the Library page's snapshot |
| Store each release's matches | Stale after the next sync, and a better reading of titles would need a rewrite of rows |
| Split a word at an apostrophe | Three Ukrainian apostrophes give three keys, and a scene name never equals the library's title |

`WorkMatchingTests.Match_by_an_identifier_alone_is_identifier_evidence`,
`WorkMatchingTests.Match_by_the_original_title_and_the_year_is_title_and_year_evidence`,
`WorkMatchingTests.Match_by_the_title_without_its_year_is_title_only_evidence`,
`WorkMatchingTests.Match_takes_the_year_of_the_title_that_matched_only`,
`WorkMatchingTests.Match_of_a_conflicting_identifier_falls_to_the_title_and_flags_the_conflict` and
`WorkMatchingTests.Match_of_a_work_two_sources_give_two_ids_matches_either_and_flags_the_conflict`
pin the rule; `TitleMatchKeyTests.For_gives_a_title_one_key_whichever_apostrophe_it_is_written_with`
and `TitleMatchKeyTests.For_drops_an_apostrophe_so_the_letters_around_it_join` the key,
`TitleMatchKeyTests.Every_word_of_a_key_is_in_the_stored_normalized_title_verbatim_once_its_apostrophes_are_removed`
the narrowing, and `ReleaseWorkClaimsTests.Read_claims_the_titles_and_the_year_the_title_names` the
reading of titles. Over HTTP,
`SearchesLibraryMatchingHttpTests.Create_lists_every_matching_work_strongest_first`,
`SearchesLibraryMatchingHttpTests.Create_matches_no_work_whose_title_only_looks_like_the_one_asked_for`,
`SearchesLibraryMatchingHttpTests.Create_with_an_id_the_work_contradicts_flags_the_conflict_and_matches_it_by_title`,
`SearchesLibraryMatchingHttpTests.Create_lists_a_work_two_sources_give_two_imdb_ids_with_the_conflict_and_both_ids`
and `SearchesLibraryMatchingHttpTests.Create_lists_the_same_film_in_two_libraries_as_two_works_each_with_its_source`
pin the library side, and
`SearchReleasesMatchingHttpTests.Releases_lists_every_work_of_the_search_a_release_matches_strongest_first`
and `SearchReleasesMatchingHttpTests.Releases_says_what_each_title_was_read_as` the releases.
`SearchesLibraryMatchingHttpTests.Create_finds_a_title_held_with_any_apostrophe_by_a_search_typed_with_another_or_none`
and `SearchReleasesMatchingHttpTests.Releases_match_a_scene_name_without_the_apostrophe_the_library_title_keeps`
pin the apostrophes through the database's narrowing too.

## Consequences

The page says what it knows and how: "Matched by IMDb id", "Matched by title and year", "Possible
match — check it", a warning per conflicting scheme, and on a release "In your library: Дюна (2021) —
matched by title and year", naming the copy in the text itself, since two copies of one title can
differ only by year and a phone shows no tooltip. A release of Toloka's matches by title and year,
since Toloka lists no ids; one
whose title names no year matches nothing there.

Known limits, left on purpose rather than guessed at: letters are compared as they are, with no
folding of accents, so "Léon" and "Leon" are two keys and a scene name that drops the accent misses;
"&" is not "and" ("Fast & Furious" and "Fast and Furious"), nor is "2" "II" ("Rocky 2" and "Rocky
II"); a release spelling a title the library holds in no language matches nothing by title. A
release is matched only to the works its search found, never to the rest of the library: a release
the search turned up for one film is never said to be another film you own. Every match is worked
out on every read, which costs nothing at a handful of works and at most 100 releases per source.

## Anti-consequence

A match is evidence for a person to read, not a link: no work is joined to a release, nothing is
stored, and nothing is done because of it. A title and year match, let alone a title alone, never
authorises a replacement (specification §3.1), and an identifier match doesn't either while its
scheme is flagged as conflicting; MAM-13 sends what the person picked, and Phase C's replacement
checks the file itself.

## Works holding the search's words (added 0.1.0, MAM-12 review)

**What the strict rule missed.** Searched for "rambo" with no year, the Library page found five
works and the search page one. The Library page finds the text anywhere in a title, an id or a file
path; the rule above lists a work only when one of its titles' keys *is* the search's key. Of the
reference library's five Rambo films only "Рембо IV", originally "Rambo" (2008), was listed, by its
title alone. "Rambo III" (1988), "Rambo: First Blood Part II" (1985) and "Rambo: Last Blood" (2019)
hold the word in a longer title, and "Рембо. Перша кров", originally "First Blood" (1982), holds it
only in its file's path, `/media/movies/Rambo. Collection/[01] Rambo. First Blood (1982) … .mkv`.
And as a release was matched only to the works its search found, a release "Rambo III (1988) BDRip"
listed beside them never said "In your library", though its title and year are exactly a work's.

**A work the rule doesn't match, but one of whose titles or file paths holds every word of the title
searched for, each whole, is listed after the matches with the evidence `Words` — never a match, at
most 20 of them, with how many more there are — and each release is matched by the unchanged rule
against every work the search lists, these included, and never by words** (Vasyl's decision 3).

**The three tiers stay as they are.** `WorkMatching.Match` is unchanged, and so is everything above
about `Identifier`, `TitleAndYear` and `TitleOnly`. `WorkMatching.MatchByWords` is asked only of a
work `Match` doesn't match (`Match(…) ?? MatchByWords(…)`), and `Words` comes after `TitleOnly`.

**The words.** Those of the search title's key (`TitleMatchKey.For`), split at its spaces, each
once. A text holds them when the words of its own key include every one: whole words, in any order,
all in one text — a title of any source, displayed or original, or one file's service path, never
gathered from two of them. "Rambo" finds "Rambo III" and the folder `Rambo. Collection`, "blood
first" finds "First Blood", and "Alien" doesn't find the title "Aliens" (though the folder
`Alien. Collection` holds it, so a search for "Alien" lists Aliens by its path). The year is not a
word and doesn't narrow the listing; ids aren't searched for words, as an id asked for already
matches by `Identifier`; a key with no word ("!!!") lists nothing. The work carries the title that
holds the words, a displayed one before an original as for a match, or none when only a path holds
them, and its conflicting schemes are flagged exactly as a match's are.

**Listed for a person to look at, and said to be no match.** The page shows these works below the
matches, under a line of their own, each with "Title contains “rambo” — not a match" or "A file path
contains “rambo” — not a match". So the look-alikes the Context refuses as matches — "Interstellar
Wars" for "Interstellar", "Spider-Man: No Way Home" for "Spider-Man" — are listed now, only here:
`SearchesLibraryMatchingHttpTests.Create_matches_no_work_whose_title_only_looks_like_the_one_asked_for`
keeps its name and now asserts that no work it lists has a match's evidence, rather than that it
lists none. Nothing about these works is stored, as for a match.

**At most 20, and the rest counted.** A word as common as "the", or "movies", which every path under
`/media/movies` holds, lists much of the library. The library side answers every match, unpaged as
above, then at most `LibraryMatcher.MaximumWorksByWords` (20) works by words in the same order — the
title the card shows in Ukrainian alphabetical order, then id — and `libraryUnlistedCount`, how many
more hold the words. The cap comes before any card is read: each candidate's card title is worked
out from its title rows the way `LibraryMapper` picks it, so a common word never reads hundreds of
cards. The page then says how many more movies hold the words and links to the Library page
searched for the same text (`/library?search=…`). The Decision's "never paged: the works one title
finds are a handful" still holds for the matches; the words listing is the capped part.

**Releases: the exact rule, against every work listed.** A release is matched by `Match` alone
against every work the search lists, the works by words included, so "Rambo III (1988) BDRip" says
"In your library: Рембо III (1988) — matched by title and year" on its own evidence. Its `evidence`
is never `Words`: a release whose title merely holds a work's words is never said to be it. The
Decision's "A release is matched against the works the search found" now reads "against the works
the search lists". The Consequences' "a release the search turned up for one film is never said to
be another film you own" now reads: never a film the search doesn't list, and never by anything
weaker than the rule. A search for "Interstellar" lists "Interstellar Wars" (2014) by words, and a
release "Interstellar Wars (2014) 1080p" it turned up is said to be that film, by title and year.

**The database still narrows, now by file paths too.** A work is also a candidate when one of its
files' `NormalizedServicePath` — `LibrarySearchText.Normalize` of the service path, the same
per-character `ToUpperInvariant` — holds the longest word of a claimed title's key once the same
apostrophes are taken out of it inside the query (nested `replace()`). The argument above holds for
a path as for a title: a path's key differs from that copy only between words and at apostrophes,
so a path holding every word holds the longest verbatim. A title holding every word holds it too, so
the titles' narrowing already keeps every work a title lists. The candidates' titles, ids and paths
are each read in one query, and only `Match` and `MatchByWords` decide.

| Rejected option | Why not |
| --- | --- |
| Find the text anywhere, as the Library page's search does | "it" finds "wITh", and a short word lists the whole library |
| Keep the rule strict and only link to the Library page | A release of Rambo III still never says you own it |
| Match a release by words too | "Rambo (2008)" would be said to be Rambo III |
| No cap on the works by words | A common word lists the library, and reads every card of it |
| Call a work by words `TitleOnly` | A look-alike becomes a possible match, "Interstellar Wars" for "Interstellar" |

`WorkMatchingTests.MatchByWords_lists_a_work_whose_title_holds_every_word_sought_as_words_evidence`,
`WorkMatchingTests.MatchByWords_lists_a_work_whose_file_path_alone_holds_every_word_without_a_matched_title`,
`WorkMatchingTests.MatchByWords_needs_every_word_whole_not_part_of_a_longer_one` and
`WorkMatchingTests.MatchByWords_flags_conflicting_ids_as_a_match_does` pin the rule. Over HTTP,
`SearchesLibraryMatchingHttpTests.Create_lists_the_works_holding_every_word_of_the_title_after_the_matches`
(the five Rambo films, one through its path),
`SearchesLibraryMatchingHttpTests.Create_lists_a_work_whose_file_path_alone_holds_the_words_without_a_matched_title`,
`SearchesLibraryMatchingHttpTests.Create_and_get_list_at_most_the_limit_of_works_by_words_and_count_the_rest`
and `SearchesLibraryMatchingHttpTests.Create_matches_no_work_whose_title_only_looks_like_the_one_asked_for`
pin the library side, and
`SearchReleasesMatchingHttpTests.Releases_match_a_work_the_search_lists_by_its_words_on_their_own_evidence`
and `SearchReleasesMatchingHttpTests.Releases_are_never_matched_to_a_work_by_words` the releases.

Known limits, left on purpose: the Library page still searches the text as typed, in one piece, so
the link can find other works than the count says — "spider man" finds no "Spider-Man" there, though
its words list it here. A word every path holds, such as the library's own folder names, lists 20
works by path and counts the rest. Words are compared as the key compares letters, so "Leon" still
doesn't find "Léon". A work by words authorises nothing, not even a check the way a `TitleOnly`
match asks for one: it is listed for a person to look at, and the Anti-consequence holds for it as
for every match.

## Square-bracketed tags that end a release's title (added 0.1.0, MAM-12 review)

**What the reading missed.** Searched for "rambo" on the reference installation (2026-10-02), Toloka
listed "Rambo: Last Blood [Extended Cut] (2019) BDRip-AVC" and "V / Rambo [Theatrical Cut] (2008)
BDRip-AVC 2xUkr/Eng", and neither said "In your library", though the library holds "Rambo: Last
Blood" (2019) and "Rambo" (2008). The year ends the title, so the edition tag written between them
became part of it: "Rambo: Last Blood [Extended Cut]" has the key `RAMBO LAST BLOOD EXTENDED CUT`
and "Rambo [Theatrical Cut]" `RAMBO THEATRICAL CUT`, which no work holds. "Rambo III (1988) BDRemux"
and "First Blood (1982)", with no tag, matched by title and year.

**Each title a release's title is read as, once trimmed, loses the square-bracketed group that ends
it, again while one does, trimmed after each — unless the group is all that is left; a round-bracketed
group is never dropped** (Vasyl's decision 4).

**At the end only, where an edition is written.** "Rambo: Last Blood [Extended Cut]" is "Rambo: Last
Blood", and "Rambo: Last Blood [Extended Cut] - [Remastered]" too. Each alternative ends where the
slash splits it, so "V / Rambo [Theatrical Cut]" is "V" and "Rambo", and "Rambo [Theatrical Cut] /
Рембо IV" is "Rambo" and "Рембо IV". A group is a `[`, text without a bracket, and a `]`; a nested
or unclosed one stays. Both readings drop it: the scene way joins the words with spaces, so
"Rambo.Last.Blood.[Extended.Cut].2019" is "Rambo Last Blood". The year is read as before — "[2014]"
ending a title is its year — and a tag after it, "[IMAX Edition]", was never part of the title.

**Kept where it may be the title.** A title that opens with or holds square brackets keeps them:
"[REC] 2 (2009)" is "[REC] 2", and "[2014] Interstellar (2014)" is still "[2014] Interstellar". A
group that is all that is left stays: "[REC] (2007)" is "[REC]", and "[REC] [Extended Cut] (2007)"
is "[REC]". Round brackets hold titles, "Birdman or (The Unexpected Virtue of Ignorance)", and are
never dropped. The Decision's "each trimmed of `-–—:|,.`" now reads "each trimmed of `-–—:|,.` and of
the square-bracketed tags that end it". Matching is unchanged: only the titles it is given are.

| Rejected option | Why not |
| --- | --- |
| Drop every square-bracketed group before the year | "[REC] 2 (2009)" reads as "2", and misses the film it is |
| Drop round-bracketed groups too | They hold titles: "Birdman or (The Unexpected Virtue of Ignorance)" |
| Leave the tag in the title, as a known limit | An edition of a film you own never says "In your library" |
| Drop a list of known edition words ("Extended Cut", "Theatrical Cut") | The list never ends, and uploaders write them in Ukrainian too |
| Match a release by words when its key misses | The Words section refuses it: "Rambo (2008)" would be Rambo III |

`ReleaseWorkClaimsTests.Read_drops_only_the_square_bracketed_tags_that_end_a_title` pins the
reading, both releases above and the titles that keep their brackets, and over HTTP
`SearchReleasesMatchingHttpTests.Releases_match_a_work_whose_title_a_square_bracketed_tag_follows`
matches the two releases to "Rambo: Last Blood" (2019) and "Rambo" (2008) by title and year.

Known limits, left on purpose: a tag that opens a title, "[HDRip] Rambo (2008)", or stands inside it
stays, and such a release still matches nothing by title; a tag holding " / " is split before it is
looked at, so "Rambo [Theatrical / Extended]" stays two broken alternatives; and a title that itself
ends in square brackets is read without them, so it misses a library title that keeps them. The
release's row still shows its whole title; only what it is read as drops the tag.

## Implementation (added 0.1.0, MAM-13)

**MAM-13 sends what the person picked, and a match decides nothing.** The Anti-consequence's
"MAM-13 sends what the person picked" holds as built. The Download dialog opens from the row of the
release the person chose and sends it to the client and destination they pick; neither the dialog's
choices nor the send read the release's matches, so no match — not even one by identifier — offers,
blocks or picks a release, a client or a destination. The dialog's summary says the existing copies
are kept, as Phase A replaces nothing. Whether a release was already sent compares it with earlier
download jobs, never with the library
([ADR 0013](0013-a-download-is-written-down-before-anything-is-sent-and-one-request-sends-it-at-most-once.md)).

## The title typed in the header (added 0.1.0, MAM-16)

The Decision's "each asked in a field of its own with the title required (Vasyl's decision 1: no
line parsed into parts)" and the rejected option "Read one search line into title, year and id"
held until MAM-16 moved the title to one search field in the header, on every page. The title now
comes from a line, and one part of that line can be the year: a year from 1900 to 2099 in round
brackets that end it, "Interstellar (2014)"
([ADR 0015](0015-only-a-bracketed-trailing-year-in-the-header-search-becomes-the-year.md)). A bare
number stays in the title ("Blade Runner 2049"), and no id is read from the line. The Search page
keeps the Year, IMDb id and TMDb id fields, to narrow the search on screen as a new search with the
same title, so the claim matched here is still a title, a year and ids. Matching is unchanged.
`search-query.test.ts` pins the reading of the line.

## Series, episodes and TV releases (added 0.2.0, MAM-32)

**The library side lists series.** The search's library side now holds movies, series and episodes of
no series
([ADR 0019](0019-a-series-is-a-work-its-episodes-point-at-its-seasons-come-from-their-numbers-and-an-episode-without-a-file-is-left-out.md)).
An episode of a series is never listed itself: when the search finds it — by a title, its IMDb id
(the search takes no TVDB id) or a file path — its series is listed once, on the strongest evidence
of the series' own and of its found episodes', the series' own winning a tie; `matchedTitle` and
`matchedIdentifiers` are then the episode's. A series none of whose episodes is stored is not
listed, as on the Library page. The cap of 20 works listed by their words counts the list's
entries, a series once however many of its episodes hold the words. A scheme that conflicts can now
also be `Tvdb` or `TmdbTv`.

**A release matches the works of its kind, and a year its seasons allow.** A TV release is matched to
the listed series only, by their own titles and ids, never an episode's, and a movie release to the
movies only; an episode of no series is matched by no release
([ADR 0020](0020-a-release-is-tv-by-its-category-matches-only-series-and-its-tracker-section-picks-the-destination-offered-first.md)).
A TV title is read by `ReleaseWorkClaims.ReadTv`, which ends the title at a bracketed span such as
"(2018-2024)" — the range the movie reading above refuses as a year — and at a season marker; a
movie's title is read as before. `WorkMatching` takes the years a claim allows (`ClaimedYears`)
instead of one year: a search and a movie release still name one year, which the title's must equal,
while a TV release's years are its seasons' (`ReleaseWorkClaims.YearsToMatch`, ADR 0020) — with
season 1 among them its first year exactly, with later seasons only or the specials any year up to
its first, and with no season any year of its span. The rule is otherwise unchanged: every match
listed strongest first, conflicts flagged, nothing stored.

**A match still picks nothing.** The MAM-13 section's "neither the dialog's choices nor the send read
the release's matches" holds: the destination the Download dialog now picks first comes from the
release's tracker section, never from a match.

`WorkMatchingTests.Match_by_the_title_takes_a_year_within_the_claimed_years`,
`SearchesTvLibraryHttpTests.Create_by_an_episodes_imdb_id_lists_its_series_with_the_episodes_id_as_the_evidence`,
`SearchesTvLibraryHttpTests.Create_by_a_title_of_the_series_and_of_its_episode_lists_the_series_on_the_stronger_its_own_on_a_tie`,
`SearchesTvLibraryHttpTests.Create_counts_a_series_whose_episodes_hold_the_words_as_one_work_of_the_limit`
and `SearchReleasesTvMatchingHttpTests.Releases_of_a_search_that_found_a_series_by_an_episode_matches_the_shows_title_not_the_episodes`
pin it.

## Recommendations (added 0.2.1, MAM-33)

**`IMetadataResolver` is implemented now, and the search still doesn't use it.** The Decision's
"`IMetadataResolver` stays unimplemented" no longer holds: TMDb's adapter implements it
([ADR 0005](0005-the-core-knows-a-service-only-through-a-registered-adapter-type.md)). The rejected
option "Ask the media server through `IMetadataResolver`" holds as it was: a search and its releases
are still matched against the snapshot the last sync imported, with no call per search, and the
Library page shows that snapshot alone. The resolver is used for recommendations only
([ADR 0023](0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)): to find
which catalog work a library work without a catalog id may be — by its IMDb id, else by its title
and year, taken only when exactly one result matches by title and year — and which work one of a
source's newest releases may be. Such a possible match is cached in the catalog
([ADR 0025](0025-the-catalog-is-kept-apart-from-the-library.md)), and never stored on the library,
shown on the Library page or used by a search.

**TMDb's lookup keeps this rule's words.** A search result is a candidate only when its title or its
original title has the query's key (`TitleMatchKey`): `TitleAndYear` when its year is the query's,
`TitleOnly` otherwise; an IMDb id TMDb files a work under is `Identifier` evidence. No distance, no
year ± 1, as above; and when two works of the year share the title, none is taken. A pack of a
show's later seasons, which names its seasons' years and not the show's, is looked up by its title
alone and taken only when exactly one show of that title began in or before its first year — the
years ADR 0020 lets such a pack match.

**A recommendation is kept out by this rule, never by less.** A catalog work is held, and so not
recommended, when the library holds its catalog id, its IMDb id or — a film against the library's
films, a show against its series — a work `WorkMatching.Match` matches by `Identifier` or
`TitleAndYear`. A match by title alone keeps nothing out: a remake of another year stays a
recommendation. The availability check of
[ADR 0024](0024-release-sources-are-asked-for-recommendations-within-a-per-user-budget.md) counts a
release for a recommended work by the same rule, reading the claim from the release's title and its
description, by `Identifier` or `TitleAndYear` only.

**The scan's names are read, and only to keep a recommendation out.** The names of the files only the
folder scan found (MAM-18) are read with `ReleaseWorkClaims.Read` — the file name without its
extension, then its folder's name — and a name that claims a catalog work by title and year keeps it
out of the lists, as the owner evidently has it. That is all they do: such a file still belongs to no
work, and the Library page, a search and a match never show it
([ADR 0021](0021-the-scan-of-the-media-folders-follows-links-only-inside-the-allowed-roots-and-keeps-one-name-of-each-file-no-library-reported.md),
ADR 0017).

The Anti-consequence holds: a match, or a recommendation, is evidence for a person to read and
authorises nothing.

`TmdbAdapterLookupTests.Lookup_by_a_title_and_year_keeps_the_titles_that_match_with_their_evidence`,
`TmdbAdapterLookupTests.Lookup_by_an_imdb_id_finds_the_film_tmdb_files_under_it`,
`RecommendationCatalogCacheHttpTests.Recompute_uses_no_title_two_works_of_the_year_share_and_caches_that_as_nothing`,
`RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_the_library_holds_by_title_and_year`,
`RecommendationExclusionsHttpTests.Recompute_matches_titles_kind_against_kind_so_a_held_show_of_the_title_leaves_a_film_in`,
`RecommendationExclusionsHttpTests.Recompute_leaves_out_a_seeds_possible_match_found_by_its_imdb_id`,
`RecommendationExclusionsHttpTests.Recompute_leaves_out_a_work_a_scan_found_files_name_claims`,
`RecommendationAvailabilityHttpTests.Check_counts_only_releases_of_the_work_by_its_kind_and_an_id_or_its_title_and_year`
and `RecommendationFeedResolutionHttpTests.Read_resolves_a_show_by_its_seasons_listing_as_a_show`
pin it.

## Linked ids, and an order that is no match (added 0.2.1, MAM-34)

**A linked id is an identifier.** A search and its releases read a work's ids as the Library page
counts them: with the owner's TMDb links applied
([ADR 0026](0026-a-tmdb-id-set-by-hand-is-the-owners-word-kept-per-library-item-and-written-into-the-library-as-ids-only.md)).
A linked id finds its work, and a release that gives it, as `Identifier` evidence; `matchedIdentifiers`
lists it with `sourceKind` `Person` and, for the owner, the name of who set it, and the IMDb id TMDb
gave for the link with TMDb's name as its source — that one only where the library holds no IMDb id.
The id a link replaces matches nothing while the library hasn't taken the link — a series' link,
never written into Jellyfin, never. A linked id beside
another library id of its scheme is two values of it, which the Decision flags as a conflict; neither
wins. An episode-to-show link gives the item none of the show's ids, so it matches nothing by them.

**A link replaces the guess.** The MAM-33 section's possible match is looked up only for a work with
no link: a linked work seeds the recommendations by its link
([ADR 0023](0023-a-recommendation-only-proposes-explains-every-reason-and-never-sends.md)).

**An order is no match.** The suggestions of ADR 0026 order TMDb's candidates for a work partly by
how alike the titles are (`TitleSimilarity`, the Dice coefficient over the letter pairs of the two
title keys) and how many years apart they are — the "distance between titles, or a year ± 1" this
Decision rejects. That order is what the owner reads first; it matches nothing, links nothing, and no
search or release sees it, so the rejected option holds. A suggestion is **confident** by this
Decision's own rule and a measurement: the one candidate of the work's kind whose title key is one of
the work's and whose year is the work's, the only such candidate, with a runtime that agrees with a
duration ffprobe measured; even then, only the owner's click links it.

`SearchesLinkedWorksHttpTests.Create_finds_a_work_by_its_linked_tmdb_id_with_the_owner_who_set_it_as_its_source`,
`SearchesLinkedWorksHttpTests.Create_as_a_member_shows_a_linked_ids_source_without_the_name_of_who_set_it`,
`SearchesLinkedWorksHttpTests.Create_finds_a_work_by_the_imdb_id_tmdb_gave_its_link_with_the_catalog_as_its_source`,
`SearchesLinkedWorksHttpTests.Create_flags_a_linked_id_beside_another_library_id_of_its_scheme_as_a_conflict`,
`SearchesLinkedWorksHttpTests.Create_never_finds_a_work_by_the_id_its_link_replaces_while_the_library_has_not_taken_the_link`,
`SearchReleasesLinkedWorksHttpTests.Releases_match_a_work_by_its_linked_tmdb_id_or_the_imdb_id_tmdb_gave_its_link`,
`TitleSimilarityTests.Of_counts_the_pairs_both_titles_have_by_dice`,
`LinkSuggestionRankingTests.Rank_makes_nothing_confident_a_year_off` and
`LinkSuggestionRankingTests.Rank_makes_nothing_confident_when_two_exact_titles_share_the_year` pin it.

**Amended 2026-10-06 (ADR 0026's Identify section, added in 0.2.1).** The words "a series' link, never
written into Jellyfin, never" in the section above had been stale since a series' link began to be
written, and no longer hold at all. The id a link replaces matches nothing while the library hasn't
taken the link, until a sync reads the link's key in the item, and a series' link is identified like a
film's: Jellyfin's Identify replaces the item's title, overview and images with TMDb's, and the id the
link leaves in the item is read back at the next sync as the library's own. The rule that a linked id is
an identifier, and that a link replaces the guess, stands.

## Duplicates (added 0.2.1, MAM-20)

**The tiers are reused for duplicates, and the third left out.** The duplicates' grouping
([ADR 0028](0028-duplicates-are-worked-out-on-every-read-in-four-classes-by-one-file-one-hash-one-registry-key-or-one-title-and-year-and-a-group-only-shows.md)) takes this Decision's identifier rule — a value trimmed and compared in any
letter case — for "another copy of the same work", by registry keys that carry the number space of
each registry, and its title key with that title's own year (`TitleMatchKey`) for a possible match,
with no distance and no year ± 1: "Alien" and "Aliens" of 1979, or "Gone" of 2001 and of 2002, are
never offered. A title alone offers nothing, and two works whose ids of one number space differ are
never offered by their title. A scanned file's name is read as a release's title is
(`ReleaseWorkClaims`, through `FileNameClaims`): its file name, then its folder's name; no year, no
title.

**A list of edition words, but not for matching.** The rejected option "Drop a list of known edition
words" (the MAM-12 review's section) still holds for matching. MAM-20's `EditionLabels` is such a
list, English and Ukrainian (Vasyl, 2026-10-07), and decides no match and keeps no two works apart:
it only labels a possible distinction between copies already grouped by their ids or their title and
year, so a word it misses costs a flag, never a wrong group or a missed one. The Anti-consequence
holds: a possible match is always for the owner to confirm, and none authorises removing anything.

`LibraryDuplicatesSameWorkHttpTests.List_never_merges_movies_with_similar_titles_or_years`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_works_with_a_similar_title_or_another_year`,
`LibraryDuplicatesPossibleHttpTests.List_never_offers_works_whose_ids_differ`,
`LibraryDuplicatesSameWorkHttpTests.List_keeps_different_cuts_apart_and_flags_their_editions_for_review`
and `EditionLabelsTests.Read_never_takes_an_edition_word_inside_another_word_or_alone_where_it_needs_two`
pin it.
