namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>Whether a download client was asked for its destinations, and how that went (MAM-11) — not
/// what it said, which a <see cref="Listed"/> result carries.</summary>
public enum ClientDestinationsStatus
{
    /// <summary>The client answered; its destinations are listed, each checked.</summary>
    Listed = 1,

    /// <summary>The download client integration is switched off, so it wasn't asked.</summary>
    ClientDisabled = 2,

    /// <summary>This version of the app can't send downloads to the integration's type, so it wasn't
    /// asked.</summary>
    TypeUnavailable = 3,

    /// <summary>A stored secret of the integration can't be decrypted with this installation's key, so it
    /// wasn't asked.</summary>
    SecretUnreadable = 4,

    /// <summary>The client was asked and couldn't answer; the error is its sentence.</summary>
    CallFailed = 5,
}
