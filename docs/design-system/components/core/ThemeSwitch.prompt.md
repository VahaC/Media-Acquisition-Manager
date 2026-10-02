Three-way Light / Dark / System theme switch; persists to localStorage `mam:theme` and toggles `.dark` on `<html>`.

```jsx
<ThemeSwitch />
<ThemeSwitch showLabels />
```

Uses the key the app's pre-paint script (frontend/index.html) already reads, so a choice survives reload with no white flash. "System" removes the key and follows `prefers-color-scheme` live. Intentional addition; the app has the same control since MAM-16 (`components/ThemeSwitch.tsx` over `lib/theme-store.ts`), in the account menu and on Profile.
