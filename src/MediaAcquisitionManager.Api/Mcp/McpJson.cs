using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaAcquisitionManager.Api.Serialization;
using ModelContextProtocol;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the JSON the MCP tools bind their arguments with, describe their input and output schemas with, and answer
/// in: the SDK's own defaults (web casing, numbers read from strings, enums by name), plus what the HTTP API's
/// controllers promise and the SDK doesn't.
/// </summary>
/// <remarks>
/// <para>
/// Nulls are written, not left out (the SDK's default drops them): Claude Code's TypeScript SDK checks
/// <c>structuredContent</c> against the tool's <c>outputSchema</c>, which lists every property of a positional record as
/// required, so a reply without its null <c>error</c> would be thrown away as invalid. Every <see cref="DateTime"/>
/// leaves with a trailing <c>Z</c> through the API's own <see cref="UtcDateTimeConverter"/>, for the bug that converter
/// describes; the schema exporter can't see through a converter, so <see cref="McpToolRegistration"/> describes those
/// properties itself. Enums go by name with no naming policy,
/// as the API's do — <c>ReadAndDownload</c>, never <c>2</c> or <c>readAndDownload</c> — so a tool's answer and the HTTP
/// API's spell a value alike.
/// </para>
/// <para>
/// A new instance per call, and a call per host: the SDK's 2.2.0 schema cache, keyed by the options instance, keeps
/// every host that registered tools with a shared instance alive (SDK issues #1904 and #1898), so a test run that boots
/// a host per class would grow by megabytes a class. The host shares its one instance between the registration and
/// <see cref="McpToolResults"/>, so a tool's answer is written exactly as its schema says.
/// </para>
/// </remarks>
public static class McpJson
{
    /// <summary>A fresh, read-only set of the options above.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            // The JSON text block keeps Cyrillic as it is: the default encoder writes "Інтерстеллар" as twelve \uXXXX
            // escapes, six times the characters for the model to read in every Ukrainian title. Safe here: the text
            // goes into a JSON-RPC string, never into HTML or a script, which is what the default escaping guards.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new UtcDateTimeConverter());
        options.MakeReadOnly();
        return options;
    }
}
