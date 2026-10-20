using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations;

/// <summary>
/// Makes text a service chose safe to put into a sentence the app shows and logs (MAM-7).
/// </summary>
/// <remarks>
/// A server name, a version or an error description is whatever the service at the owner's address
/// sends — a wrong address or a hostile service included. Every remote string that enters a message
/// goes through <see cref="Clean"/> first. MAM-47 — the rules moved to <see cref="VendorText.Clean"/>,
/// which a sentence's vendor slot takes; this stays for a service's data that is no sentence (a server
/// name, a version).
/// </remarks>
internal static class RemoteText
{
    /// <summary>
    /// <paramref name="value"/> masked, cleaned and cut as <see cref="VendorText.Clean"/> does. Empty for <c>null</c>.
    /// </summary>
    public static string Clean(string? value, int maximumLength, string?[] secrets) =>
        VendorText.Clean(value, maximumLength, secrets).Text;
}
