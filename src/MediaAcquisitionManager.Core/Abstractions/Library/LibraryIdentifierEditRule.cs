namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — when a <see cref="LibraryIdentifierEdit"/> changes the item.</summary>
public enum LibraryIdentifierEditRule
{
    /// <summary>Whatever the item holds: the link's own TMDb id.</summary>
    Always = 1,

    /// <summary>
    /// Only when the item holds no non-blank value of the scheme — a malformed one counts as held — so the app never
    /// overwrites an id the library has ("IMDb only where the library has none").
    /// </summary>
    OnlyWhereAbsent = 2,

    /// <summary>
    /// Only when the item holds <see cref="LibraryIdentifierEdit.Expected"/> (or none, when that is <c>null</c>): a
    /// restore changes back only what still holds what the app wrote, leaving alone anything someone changed since
    /// (decision 13).
    /// </summary>
    OnlyWhereExpected = 3,
}
