using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// Answers for a <see cref="RecordingHttpHandler"/> to give: the real ones in
/// <see cref="RepositoryPaths.VendorSamples"/> (MAM-6), and the small ones a test writes inline.
/// </summary>
public static class VendorResponses
{
    /// <summary>
    /// The sample at <paramref name="relativePath"/> under <c>VendorSamples/</c> (such as
    /// <c>Jellyfin/system-info.json</c>), byte for byte, as <paramref name="mediaType"/> (which may
    /// carry a charset).
    /// </summary>
    public static HttpResponseMessage Sample(string relativePath, string mediaType, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(RepositoryPaths.VendorSamples, relativePath)));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return new HttpResponseMessage(status) { Content = content };
    }

    /// <summary><paramref name="body"/> as UTF-8 <paramref name="mediaType"/>.</summary>
    public static HttpResponseMessage Text(string body, HttpStatusCode status, string mediaType = "text/plain") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    /// <summary>A redirect to <paramref name="location"/>, which may be relative.</summary>
    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>
    /// <paramref name="response"/> with one more <c>Set-Cookie</c> header, taken verbatim — so a test
    /// can send a cookie no real server would.
    /// </summary>
    public static HttpResponseMessage WithSetCookie(HttpResponseMessage response, string setCookie)
    {
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return response;
    }
}
