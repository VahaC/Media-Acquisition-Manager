namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>
/// How a call to an integration went. One taxonomy for every call to any integration, so the UI and
/// the error handling never depend on one product's fields (Phase D acceptance).
/// </summary>
public enum IntegrationCallStatus
{
    /// <summary>It answered the way this type of service does.</summary>
    Succeeded = 1,

    /// <summary>No connection: the name wasn't found, the connection was refused or reset, or the
    /// secure connection failed.</summary>
    Unreachable = 2,

    /// <summary>No answer within the client's timeout, or no connection accepted within its connect
    /// timeout.</summary>
    TimedOut = 3,

    /// <summary>The credentials were refused, or are missing where the service needs them.</summary>
    AuthenticationFailed = 4,

    /// <summary>Understood, but it won't serve now: the address is banned, the API is switched off, or
    /// the call was rate limited.</summary>
    Refused = 5,

    /// <summary>The service failed on its side: a 5xx, or an error it reports as its own.</summary>
    ServiceError = 6,

    /// <summary>Not what this type of service sends: a wrong address or product, a redirect, a
    /// malformed or huge body.</summary>
    UnexpectedResponse = 7,

    /// <summary>Kind contracts only: the service has no such item or job.</summary>
    NotFound = 8,
}
