using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

/// <summary>
/// Jellyfin behind the core's contracts (Adapter, ADR 0005). MAM-7 builds the registration and the
/// connection test; MAM-8 adds the media library contract to this class.
/// </summary>
internal sealed class JellyfinAdapter(IHttpClientFactory httpClients) : HttpIntegrationAdapter(httpClients, HttpClientName)
{
    /// <summary>The stored type id — part of the on-disk contract (ADR 0005).</summary>
    public const string TypeId = "jellyfin";

    /// <summary>The named client this adapter's requests go through, so a test can swap its handler.</summary>
    public const string HttpClientName = TypeId;

    /// <summary>The longest server name or version a sentence repeats.</summary>
    private const int RemoteValueMaximumLength = 100;

    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string ForbiddenError =
        "Jellyfin accepted the key but won't show it the server's details (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.";

    private const string NotFoundError =
        "Jellyfin wasn't found at this address (404 Not Found for /System/Info). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    /// <inheritdoc/>
    public override IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        "Jellyfin",
        IntegrationKind.MediaLibrary,
        UserName: CredentialRequirement.NotUsed,
        Password: CredentialRequirement.NotUsed,
        ApiKey: CredentialRequirement.Required,
        ReportsFilePaths: true);

    /// <summary>
    /// <c>GET /System/Info</c> with the key. Not <c>/System/Info/Public</c>: it answers without a key and
    /// so proves nothing about it.
    /// </summary>
    protected override async Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Resolve("System/Info"));
        // The Authorization scheme Jellyfin 12 reads by default. X-Emby-Token and api_key= work only
        // while legacy authorization is switched on, and 12 switches it off (VendorSamples README);
        // api_key= would also put the key into every URI a proxy logs. The key rules keep the value
        // free of quotes and backslashes, so it can't break out of the quoted string.
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => await ReadSystemInfoAsync(response, CallSecrets(settings, sessionSecrets), cancellationToken),
            HttpStatusCode.Unauthorized => ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError),
            HttpStatusCode.Forbidden => ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, ForbiddenError),
            HttpStatusCode.NotFound => ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError),
            _ => DescribeUnexpectedStatus(response, settings, sessionSecrets),
        };
    }

    /// <summary>
    /// Reads three named properties and nothing else (an allowlist): the body also carries the
    /// server's local address and its file-system paths, which have no business in a message. Each is
    /// masked of <paramref name="secrets"/> before it is cut to length.
    /// </summary>
    private static async Task<ConnectionTestResult> ReadSystemInfoAsync(
        HttpResponseMessage response, string?[] secrets, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return NotJellyfin();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return NotJellyfin();

            // Checked before the version: another product that answers /System/Info (Emby does) is
            // worth naming, whatever else its answer lacks.
            if (root.TryGetProperty("ProductName", out var productElement) && productElement.ValueKind != JsonValueKind.Null)
            {
                if (productElement.ValueKind != JsonValueKind.String)
                    return NotJellyfin();
                var product = RemoteText.Clean(productElement.GetString(), RemoteValueMaximumLength, secrets);
                if (product.Length == 0)
                    return NotJellyfin();
                if (!product.StartsWith("Jellyfin", StringComparison.OrdinalIgnoreCase))
                    return ConnectionTestResult.Failed(
                        IntegrationCallStatus.UnexpectedResponse, $"The service at this address is {product}, not Jellyfin.");
            }

            var version = StringProperty(root, "Version", secrets);
            if (version.Length == 0)
                return NotJellyfin();

            var serverName = StringProperty(root, "ServerName", secrets);
            return ConnectionTestResult.Succeeded(serverName.Length > 0
                ? $"Connected to Jellyfin {version} ({serverName})."
                : $"Connected to Jellyfin {version}.");
        }
    }

    private static string StringProperty(JsonElement root, string name, string?[] secrets) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? RemoteText.Clean(element.GetString(), RemoteValueMaximumLength, secrets)
            : "";

    private static ConnectionTestResult NotJellyfin() =>
        ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotJellyfinError);
}
