using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the two answers an MCP tool gives: a success, with a one-sentence summary and the data both as text and as
/// <c>structuredContent</c>; and an expected failure, the sentence alone, marked <c>isError</c>.
/// </summary>
/// <remarks>
/// <para>
/// The data goes out twice on purpose: a client that reads <c>structuredContent</c> checks it against the tool's output
/// schema, and one that doesn't — or a model shown only the text — still gets the same JSON (the specification's
/// "SHOULD also return the serialized JSON in a TextContent block"). The summary comes first, so a model reading the
/// text sees the gist before the numbers.
/// </para>
/// <para>
/// An expected failure is returned, never thrown (the house rule): the SDK turns an exception into "An error occurred
/// invoking '…'." and logs it at Error, so the user would read nothing useful while the log raised an alarm about a
/// missing job. A failure has no <c>structuredContent</c>: there is no data to check against the schema.
/// </para>
/// <para>
/// A singleton holding the host's <see cref="McpJson"/> options — the same instance the tools' schemas and arguments go
/// by. Tools take it in their constructor.
/// </para>
/// </remarks>
/// <param name="options">The host's MCP JSON options (<see cref="McpJson.CreateOptions"/>).</param>
public sealed class McpToolResults(JsonSerializerOptions options)
{
    /// <summary>A success: <paramref name="summary"/>, one short English sentence saying what the answer holds ("3 works
    /// match “Dune”; this page lists 3."), then <paramref name="data"/> as JSON, also as the structured content.</summary>
    /// <typeparam name="TData">The tool's <c>OutputSchemaType</c>, so the content is what its schema describes.</typeparam>
    public CallToolResult Success<TData>(string summary, TData data)
    {
        var structured = JsonSerializer.SerializeToElement(data, options);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = summary },
                new TextContentBlock { Text = structured.GetRawText() },
            ],
            StructuredContent = structured,
            IsError = false,
        };
    }

    /// <summary>An expected failure: <paramref name="sentence"/>, saying what went wrong and what to do instead, with
    /// <c>isError</c> set. Static, as the access gate and the argument filter answer with it too.</summary>
    public static CallToolResult Error(string sentence) => new()
    {
        Content = [new TextContentBlock { Text = sentence }],
        IsError = true,
    };
}
