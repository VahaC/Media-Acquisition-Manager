using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaAcquisitionManager.Api.Serialization;

/// <summary>
/// Shared options for JSON written straight to <c>Response.Body</c> outside MVC (the health endpoints'
/// <c>HealthResponseWriter</c>), which so bypasses both the converters configured on
/// <c>AddControllers().AddJsonOptions(...)</c> and the encoder MVC's output formatter adds. Mirrors that config
/// — camelCase, enums-as-strings, UTC <see cref="DateTime"/>s with a trailing <c>Z</c> (see
/// <see cref="UtcDateTimeConverter"/>), and non-ASCII text as raw UTF-8 — so an answer written here reads like a
/// controller's.
/// </summary>
public static class StreamingJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // MAM-48 F5 — what MVC's output formatter uses when no encoder is set: without it the default encoder sent a
        // Ukrainian description as \uXXXX escapes, six bytes for every Cyrillic letter. Safe here as there: the body is
        // application/json, never embedded in HTML or a script, which is what the default escaping guards.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(),
            new UtcDateTimeConverter(),
        },
    };
}
