One release from a source on the Search page (current design).

```jsx
<ReleaseRow release={{ title: "Interstellar.2014.2160p.UHD.BDRemux", source: "Toloka", size: "78.4 GiB", seeders: 12, peers: 3, published: "12 Mar 2024", claimed: { resolution: 2160, medium: "BDRemux", codec: "HEVC", ranges: ["HDR10"], audio: ["Ukrainian","English"] } }} />
```

Download opens the Download dialog that sends the release to a download client (MAM-13); the kit's `ReleaseRow.jsx` still greys it out unless `downloadEnabled` is set.
