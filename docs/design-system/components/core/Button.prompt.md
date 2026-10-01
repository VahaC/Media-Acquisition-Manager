The app button (.ui-button) — primary for the one main action, outline for row actions, ghost in the header, danger for Remove.

```jsx
<Button icon="Plus">Add integration</Button>
<Button variant="outline" size="sm" icon="PlugZap">Test</Button>
<Button variant="danger" size="sm" icon="Trash2">Remove</Button>
```

Row actions are always `outline` + `sm` with an icon; on phones labels hide (label-text). `loading` swaps the icon for a spinning LoaderCircle ("Searching…").
