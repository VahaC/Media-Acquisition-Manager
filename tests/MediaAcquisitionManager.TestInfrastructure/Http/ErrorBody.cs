using System.Text.Encodings.Web;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-47 (ADR 0042) — the exact bytes of an error body, <c>{"error":"…","code":"…"}</c>, for the tests that compare a
/// whole answer: the anti-probing tests prove two causes answer the same bytes. In the pseudo pass the sentence is
/// wrapped the way the server wraps it.
/// </summary>
public static class ErrorBody
{
    // The serializer ASP.NET Core writes answers with: web names and the relaxed encoder, so “ ” ’ & + ' travel as they
    // are instead of as \uXXXX (ErrorBodyHttpTests pins it against a real answer).
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The body the API writes for a one-sentence error whose English is <paramref name="english"/> and whose key
    /// is <paramref name="code"/>, in this pass's language (English, or wrapped in the pseudo pass).</summary>
    /// <remarks>For a sentence that nests another the pseudo pass wraps each level, which this can't know: compare such a
    /// body through <c>ReadErrorBodyAsync</c> instead.</remarks>
    public static string Json(string english, string code)
    {
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(code);
        var error = TestLanguage.IsPseudo ? MessageRenderer.PseudoOpen + english + MessageRenderer.PseudoClose : english;
        return JsonSerializer.Serialize(new ApiErrorResponse(error, code), Options);
    }
}
