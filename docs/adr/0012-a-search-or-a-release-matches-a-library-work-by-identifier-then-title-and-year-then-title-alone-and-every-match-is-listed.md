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
