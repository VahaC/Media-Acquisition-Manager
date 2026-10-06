namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — why the app refused to identify an item before sending anything
/// (<see cref="LibraryIdentifyOutcome.Refused"/>).</summary>
public enum LibraryIdentifyRefusal
{
    /// <summary>The item's library saves beside the media (metadata, an NFO file, trickplay or downloaded subtitles), so
    /// identifying the item could change files in the owner's media folders. Files and folders are never touched.</summary>
    LibraryWritesFiles = 1,

    /// <summary>The library, or its options, couldn't be determined: the app fails closed.</summary>
    LibraryUnknown = 2,

    // Not named for the word the owner reads in the sentence: the convention test of the capability forbids that word in
    // any member name (a name for locking is a decision for an ADR), and "protected" is what the library's own wording
    // for it means.
    /// <summary>The item is locked in the library — the whole item, its name or its overview — so the library wouldn't
    /// take the result, and the images Apply deletes first would be lost for nothing. Not the request's
    /// <see cref="LibraryIdentifyRequest.ProtectHeldIds"/>.</summary>
    ItemProtected = 3,

    /// <summary>The request protects held ids and the item holds an id of its own that the identification would discard.</summary>
    ConflictingId = 4,

    /// <summary>The library holds an item of another type under the id than the request's kind.</summary>
    KindNotIdentifiable = 5,
}
