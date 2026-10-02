# 15. Only a bracketed year that ends the line typed in the header's search becomes the search's year

Status: accepted (0.1.0, MAM-16)

## Context

MAM-12 asked for the title in a field of its own, beside the year, the IMDb id and the TMDb id
(Vasyl's decision 1 in ADR 0012: "no line parsed into parts"). MAM-16's approved layout moves the
title to one search field in the header, on every page. Enter there opens the Search page, which
makes the search at once (ADR 0011, MAM-16 section). The page keeps the year and the ids, but only
to narrow a search that is already on screen, so the first search of a title has nothing but the
line typed in the header.

The layout's brief asks for a four-digit year from 1900 to 2099 at the end of that line, bare or in
brackets ("Interstellar 2014", "Interstellar (2014)"), to be sent as the year. A bare year goes
wrong on real titles:

- **Titles end in such numbers.** "Blade Runner 2049", "Wonder Woman 1984", "Death Race 2000" and
  "Godzilla 2000" would be searched as "Blade Runner" of 2049 and so on: no work of the library
  matches that title and year, and every other film called "Blade Runner" matches by title alone.
- **The number can't be put back.** The server stores the title it was given, and the page narrows
  with that stored title, so clearing the Year field searches "Blade Runner" with no year. Typing
  the line again in the header splits it again.

Not splitting at all goes wrong the other way. "Interstellar (2014)" is how people write a film with
its year, and how Toloka's titles write it ("Інтерстеллар / Interstellar (2014)"). Kept whole, it is
a title key no work holds (ADR 0012), so the search would match nothing in the library.

## Decision

**Only a year from 1900 to 2099 in round brackets that end the line, after some title, becomes the
search's year. Everything else typed in the header is the title, a bare number included** (Vasyl's
answer to the layout's open question, 2 Oct 2026).

**What is read.** `parseSearchQuery` (`frontend/src/lib/search-query.ts`) trims the line and takes
`(YYYY)` at its very end: matching brackets, nothing after them, any spaces before them, and title
text before that. "Interstellar (2014)" and "Interstellar(2014)" are "Interstellar" of 2014;
"Blade Runner 2049 (2017)" is "Blade Runner 2049" of 2017. The digits are ASCII only, as the server
reads a year.

**What stays the title.** "Blade Runner 2049", "Interstellar 2014", "1917" and "(2014)"; brackets
that don't match ("Interstellar (2014", "Interstellar 2014)"); square brackets ("Interstellar
[2014]"); a bracketed year with more after it ("Interstellar (2014) Remastered"); and a bracketed
number outside 1900–2099 or with other digits. An IMDb or TMDb id typed there is a title too: the
server takes "tt0816692" as text and answers 201, so the page says "Results for “tt0816692”." with
whatever the sources list for those letters. The page's own fields take ids.

| Rejected option | Why not |
| --- | --- |
| A bare year at the end too, as the brief asked | Splits "Blade Runner 2049", "Wonder Woman 1984" and "Death Race 2000", and the number can't be put back |
| No year read from the line | "Interstellar (2014)" would be a title no work of the library holds |
| A bracketed year before the end of the line | Nothing then says where the title ends: "Interstellar (2014) Remastered" |
| Square brackets too | In a release title they hold tags ("[Extended Cut]", ADR 0012); one written form is enough to learn |
| Read an IMDb or TMDb id from the line | Text the server accepts as a title would change meaning; ids have fields of their own on the page |

`search-query.test.ts` pins the reading ("takes the bracketed year that ends … as the year, and the
rest as the title", "keeps a year that is not in brackets after a title as part of the title") and
`SearchPage.test.tsx` the search it makes ("makes the search in ?q= at once, a bracketed year ending
it as the year, and puts its id in the address in its place").

## Consequences

"Interstellar 2014" searches for the title "Interstellar 2014": no work of the library matches it by
title (a file path holding both words lists the film as no match, ADR 0012), and the release sources
are asked for both words. So the bare Search page teaches the bracketed form
("Add the year in brackets after the title to narrow it: “Interstellar (2014)”."), and so does the
field's placeholder, "Search a movie by title, e.g. Interstellar (2014)", where the brief wrote
"e.g. Interstellar 2014". The page's Year field narrows any search, a title ending in a number
included.

While the search is made, the page's Year field shows the year the line gave. Once it is made, the
header's field shows the stored title, "Interstellar", without the year, which stays in the Year
field below. Pressing Enter on that title in the header again searches without the year.

## Anti-consequence

This reads only the line typed in the header. How a release's title is read — the first bracketed
year, else the scene's bare year from the second word on (ADR 0012) — is unchanged: a release title
goes on with quality words after its year, which say where the title ends, and a search line
doesn't.
