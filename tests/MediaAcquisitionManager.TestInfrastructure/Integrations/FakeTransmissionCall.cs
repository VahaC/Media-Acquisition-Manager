using System.Text.Json.Nodes;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one RPC a <see cref="FakeTransmissionServer"/> read: a <c>POST</c> to its RPC address whose body is a JSON
/// object and that passed every check before it (address, credentials, path, host name, session id), whatever it
/// answered. A request one of those checks refused was never read, as Transmission never reads it, so it is no call.
/// </summary>
/// <param name="Method">The <c>method</c> as sent; <c>""</c> when the body named none.</param>
/// <param name="ArgumentNames">The name of every argument, in the order sent — so a test can assert that nothing but
/// what it expects went out.</param>
/// <param name="Arguments">A copy of the <c>arguments</c> object; empty when none was sent.</param>
/// <param name="SessionId">The <c>X-Transmission-Session-Id</c> it carried.</param>
/// <param name="Authorization">The <c>Authorization</c> header it carried, verbatim; <c>null</c> for none.</param>
/// <param name="Tag">The <c>tag</c> when it was an integer, which the answer echoes; <c>null</c> otherwise.</param>
public sealed record FakeTransmissionCall(
    string Method,
    IReadOnlyList<string> ArgumentNames,
    JsonObject Arguments,
    string? SessionId,
    string? Authorization,
    long? Tag);
