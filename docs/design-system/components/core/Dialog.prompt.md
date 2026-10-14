Centered modal (Radix look): title, muted description, body, right-aligned footer — primary first, Cancel after.

```jsx
<Dialog title="Remove Jellyfin?" description="…" onClose={close} footer={<><Button variant="destructive">Remove</Button><Button variant="outline">Cancel</Button></>} />
```

Settings dialogs 28rem; confirm 26rem; Release details 64rem two-column (MAM-31: 64rem is the cap of every dialog with a long list, its list in auto-fill columns).
