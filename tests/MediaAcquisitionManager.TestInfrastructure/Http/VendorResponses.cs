using System.Globalization;
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

    /// <summary>
    /// The answer recorded at <paramref name="relativePath"/> under <c>VendorSamples/</c> as its status
    /// line and headers (MAM-10): for an answer whose status and headers are all it says, such as
    /// Prowlarr's bare 401 to a wrong key. Its body is empty, as recorded.
    /// </summary>
    public static HttpResponseMessage Recorded(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.VendorSamples, relativePath));
        // "HTTP/1.1 401 Unauthorized": the code is the second word.
        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(lines[0].Split(' ')[1], CultureInfo.InvariantCulture))
        {
            Content = new ByteArrayContent([]),
        };
        foreach (var line in lines.Skip(1).Where(line => line.Contains(':', StringComparison.Ordinal)))
        {
            var name = line[..line.IndexOf(':', StringComparison.Ordinal)];
            var value = line[(name.Length + 1)..].Trim();
            if (!response.Headers.TryAddWithoutValidation(name, value))
                response.Content.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
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
