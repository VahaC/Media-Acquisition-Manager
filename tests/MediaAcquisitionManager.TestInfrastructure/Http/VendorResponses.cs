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
    /// line and headers (MAM-10), and — MAM-11 — the body after the blank line that ends the headers, as
    /// recorded: qBittorrent's <c>403 Forbidden</c> to a session it doesn't know says <c>Forbidden</c>. A
    /// recording without a blank line, such as Prowlarr's bare 401 to a wrong key, has an empty body.
    /// </summary>
    public static HttpResponseMessage Recorded(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.VendorSamples, relativePath));
        var blank = Array.FindIndex(lines, line => line.Length == 0);
        var headers = blank < 0 ? lines[1..] : lines[1..blank];
        // The body's lines as the file keeps them, joined with LF and without the file's final line
        // break: "Forbidden" is the 9 bytes its recorded Content-Length says.
        var body = blank < 0 ? "" : string.Join('\n', lines[(blank + 1)..]);
        // "HTTP/1.1 401 Unauthorized": the code is the second word.
        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(lines[0].Split(' ')[1], CultureInfo.InvariantCulture))
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        foreach (var line in headers.Where(line => line.Contains(':', StringComparison.Ordinal)))
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
    /// MAM-30 — a 200 whose body never arrives: the request was read, and the answer doesn't come within the client's
    /// timeout, or before the caller gives up — for a test of a call that must not be sent again.
    /// </summary>
    public static HttpResponseMessage Stalled() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new System.IO.Pipelines.Pipe().Reader.AsStream()) };

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
