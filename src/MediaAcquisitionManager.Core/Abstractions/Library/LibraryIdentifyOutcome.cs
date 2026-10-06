namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — how one identification was decided, when the library was reached and the call itself didn't fail.</summary>
public enum LibraryIdentifyOutcome
{
    /// <summary>Apply was sent and the item read again shows the match. <see cref="LibraryIdentification.Sent"/> is
    /// <c>true</c>.</summary>
    Identified = 1,

    /// <summary>The item already shows the result — its name, year, id, a primary image when the result has one and the
    /// overview when the result has one — so Apply was not sent: it would delete the item's images for nothing.
    /// <see cref="LibraryIdentification.Sent"/> is <c>false</c>.</summary>
    AlreadyIdentified = 2,

    /// <summary>The library's provider has no result carrying exactly that id, or more than one. Nothing was sent.</summary>
    NoMatch = 3,

    /// <summary>The app refused before sending anything; <see cref="LibraryIdentification.Refusal"/> says which rule.</summary>
    Refused = 4,

    /// <summary>Apply left the app (answered 2xx or 5xx, lost, or timed out) and the item read again doesn't show the
    /// match, or couldn't be read. <see cref="LibraryIdentification.Sent"/> is <c>true</c>. Never "identified".</summary>
    NotConfirmed = 5,
}
