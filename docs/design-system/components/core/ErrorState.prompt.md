Block shown in place of a list or panel whose first load failed: title, the server's sentence, Retry.

```jsx
<ErrorState title="Could not load the library." onRetry={refetch}>The server could not be reached. Check that it is running, then try again.</ErrorState>
```

Use only when there is no data to show. A failed refresh with data on screen is a `Banner` above the data; a failed field is `Field error`. Dashed red-tinted border, red icon disc; text stays foreground/muted.
