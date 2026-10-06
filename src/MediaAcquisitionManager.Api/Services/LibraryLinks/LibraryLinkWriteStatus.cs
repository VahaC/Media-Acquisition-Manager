namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — where one identification of a claimed link (<see cref="ILibraryLinkWriter.WriteAsync"/>) left it.</summary>
public enum LibraryLinkWriteStatus
{
    /// <summary>The library's own result is on the item: the read after the identification showed it, or the item showed it
    /// already and nothing was sent.</summary>
    Written = 1,

    /// <summary>The item isn't identified (yet): the library refused, found no match, couldn't be reached, didn't confirm
    /// what it was sent, or is switched off; the link's error says why.</summary>
    NotWritten = 2,

    /// <summary>The link isn't identified in its library by rule — an episode's link that has no id of its own to identify
    /// by, a link on a kind of item the library doesn't identify (Jellyfin: an episode item), or one of a library type
    /// that can't identify at all.</summary>
    KeptInApp = 3,

    /// <summary>The link was no longer this identification's to settle — removed, or claimed again — so nothing was changed
    /// on it.</summary>
    ClaimLost = 4,
}
