Why a library copy matched the search: evidence, matched title, id conflicts.

```jsx
<MatchBadges evidence="Identifier" schemes={["IMDb"]} matchedTitle="Інтерстеллар, 2014" />
```

TitleOnly → warning tone ("Possible match — check it").

Words → not a match: a work listed below the matches because one of its titles or file paths holds
every word searched for. One outline badge with the TextSearch icon, "Title contains “rambo” — not a
match", or "A file path contains “rambo” — not a match" when no title holds the words, with a hint on
hover; the matched title and conflict badges as usual. Never on a release row: a release is never
matched by words. The bundled component above doesn't draw this state yet; the app's
`components/search/MatchBadges.tsx` does.
