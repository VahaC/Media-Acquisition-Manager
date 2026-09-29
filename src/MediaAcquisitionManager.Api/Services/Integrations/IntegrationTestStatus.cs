namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>How a request to test a connection ended — not what the service answered, which a
/// <see cref="Tested"/> result carries whatever it was (ADR 0005).</summary>
public enum IntegrationTestStatus
{
    /// <summary>The test ran; the response says what the service answered.</summary>
    Tested = 1,

    /// <summary>No integration has the id the request addressed.</summary>
    NotFound = 2,

    /// <summary>The integration a draft test edits no longer exists; the error says so.</summary>
    EditedIntegrationMissing = 3,

    /// <summary>A field of the draft breaks a rule, which the error names; no service was called.</summary>
    Invalid = 4,

    /// <summary>This version of the app no longer connects to the integration's type; no service was
    /// called.</summary>
    AdapterUnavailable = 5,

    /// <summary>A stored secret the test needs can't be decrypted with this installation's key; no
    /// service was called.</summary>
    SecretUnreadable = 6,
}
