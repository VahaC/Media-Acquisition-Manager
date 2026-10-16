using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp;

public static class McpToolRegistration
{
    /// <summary>
    /// MAM-45 — registers every <c>[McpServerTool]</c> method of this assembly's <c>[McpServerToolType]</c> classes, as the
    /// SDK's <c>WithToolsFromAssembly</c> does — a singleton tool each, its class built per call with the request's
    /// services — and also tells the schema, by type, to leave out the caller (<see cref="ClaimsPrincipal"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDK's own registration leaves the caller out by looking its <see cref="ParameterInfo"/> up in a dictionary
    /// (Microsoft.Extensions.AI 10.8.3, <c>ReflectionAIFunctionDescriptor</c>), keyed by the instance the runtime handed
    /// out earlier. The runtime's cache of a method's parameters fills without a lock, so two hosts building their tools
    /// at once — the test suite's classes in parallel — can get different instances for one parameter; the lookup then
    /// misses and the schema lists <c>user</c>, a required object of every claim type, which a model would try to fill
    /// in. <c>McpToolListHttpTests</c> caught it on some tools in some runs. The schema options' own
    /// <c>IncludeParameter</c> is asked whenever that lookup misses, so a check by type decides the same either way.
    /// </para>
    /// <para>
    /// <paramref name="serializerOptions"/> is the host's one <see cref="McpJson"/> instance, which binds the arguments,
    /// describes the schemas and writes the answers.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithMediaAcquisitionManagerTools(this IMcpServerBuilder builder, JsonSerializerOptions serializerOptions)
    {
        var schemaOptions = new AIJsonSchemaCreateOptions
        {
            IncludeParameter = parameter => parameter.ParameterType != typeof(ClaimsPrincipal),
            TransformSchemaNode = DescribeDateTimes,
        };

        var tools =
            from type in typeof(McpToolRegistration).Assembly.GetTypes()
            where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
            from method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where method.GetCustomAttribute<McpServerToolAttribute>() is not null
            select (Type: type, Method: method);

        foreach (var (type, method) in tools)
        {
            // Lazily, as the SDK does: the root services tell its binder which parameters DI fills.
            builder.Services.AddSingleton(services =>
            {
                var options = new McpServerToolCreateOptions
                {
                    Services = services,
                    SerializerOptions = serializerOptions,
                    SchemaCreateOptions = schemaOptions,
                };
                return method.IsStatic
                    ? McpServerTool.Create(method, target: null, options)
                    : McpServerTool.Create(
                        method,
                        request => ActivatorUtilities.CreateInstance(
                            request.Services ?? throw new InvalidOperationException("An MCP tool call carries no services."), type),
                        options);
            });
        }

        return builder;
    }

    /// <summary>
    /// Describes each <see cref="DateTime"/> property of an object's schema as "a string in the date-time format" (or
    /// null, for a <c>DateTime?</c>), where the schema exporter writes <c>true</c> — "any value" — because the type has a
    /// converter of its own (<see cref="McpJson"/>'s <c>UtcDateTimeConverter</c>) it can't see through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not cosmetic: the MCP TypeScript SDK (1.32.1), which Claude Code and mcp-remote run on, takes a property's schema
    /// only as an object, so one <c>true</c> fails its check of the whole <c>tools/list</c> answer and the assistant gets no
    /// tool at all. The live check of 2026-10-16 found it; the SDK's own C# client doesn't look.
    /// </para>
    /// <para>
    /// From the object's node, by its properties' types, rather than from the property's own node: the exporter asks
    /// nothing for a property it describes as <c>true</c> with a default value (<c>LibraryIdentifierDto.SetUtc = null</c>
    /// comes out <c>{"default":null}</c>), so only the object sees every one. Whatever else the node holds, such as that
    /// default, is kept; every other property is left as the exporter wrote it.
    /// </para>
    /// </remarks>
    private static JsonNode DescribeDateTimes(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (schema is not JsonObject objectSchema || objectSchema["properties"] is not JsonObject properties)
            return schema;

        foreach (var property in context.TypeInfo.Properties)
        {
            var nullable = Nullable.GetUnderlyingType(property.PropertyType) == typeof(DateTime);
            if ((!nullable && property.PropertyType != typeof(DateTime)) || !properties.TryGetPropertyValue(property.Name, out var described))
                continue;

            JsonNode type = nullable ? new JsonArray("string", "null") : JsonValue.Create("string");
            if (described is JsonObject node)
            {
                node["type"] = type;
                node["format"] = "date-time";
            }
            else
            {
                properties[property.Name] = new JsonObject { ["type"] = type, ["format"] = "date-time" };
            }
        }

        return schema;
    }
}
