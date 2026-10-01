Labelled chips for tracks ("Audio") or claims ("Claimed"); Facts = a gap-separated 12px fact line.

```jsx
<ChipRow label="Audio" chips={["Ukrainian · AC-3 · 5.1 · default"]} />
<Facts verified items={["2:49:03","3840×2160","HEVC Main 10","HDR10"]} />
```

Unknown values are named ("HDR unknown"), never omitted. verified = ffprobe facts in text colour; listing facts are muted.
