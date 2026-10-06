namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — how putting back what the app wrote for a link before it is removed went
/// (<see cref="ILibraryLinkWriter.RestoreAsync"/>).</summary>
public enum LibraryLinkRestoreStatus
{
    /// <summary>Nothing to put back, and nothing was sent: the link was never sent to its library, its item is gone from
    /// the library's list, the library type can't set ids or doesn't write the item's kind (Jellyfin: a series), or the
    /// item no longer holds what the app wrote.</summary>
    NotNeeded = 1,

    /// <summary>The item's ids were sent back as the app found them (decision 13); the write says what was read and
    /// left.</summary>
    Restored = 2,

    /// <summary>The library no longer has the item: nothing is left to put back.</summary>
    ItemGone = 3,

    /// <summary>The library couldn't be read or changed; the error says why. The link must stay.</summary>
    Failed = 4,

    /// <summary>The library integration is switched off, so it can't be changed back; the error says what to do. The link
    /// must stay.</summary>
    SwitchedOff = 5,
}
