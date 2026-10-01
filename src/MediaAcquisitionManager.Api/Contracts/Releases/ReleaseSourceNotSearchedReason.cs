namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// Why a search didn't ask one release source (MAM-10). Where a connection test or a sync answers 409
/// for these, one source of a search can't fail the others, so the source says it instead.
/// </summary>
public enum ReleaseSourceNotSearchedReason
{
    /// <summary>Its stored API key can't be read with this installation's encryption key; the owner
    /// enters it again.</summary>
    SecretUnreadable = 1,

    /// <summary>This version of the app can't search through its type (ADR 0005).</summary>
    TypeUnavailable = 2,

    /// <summary>It offers no search this app can use: no search by text, and no movie search by text or
    /// by an id that was asked for.</summary>
    NoUsableSearch = 3,
}
