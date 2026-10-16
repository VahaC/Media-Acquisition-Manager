using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — answers a <c>tools/call</c> whose arguments don't fit the tool's parameters with
/// <see cref="McpToolErrors.ArgumentsDontFit"/>, instead of the SDK's "An error occurred invoking '…'.", which tells the
/// assistant nothing it could correct.
/// </summary>
/// <remarks>
/// <para>
/// The SDK binds the arguments as it invokes the tool, and 2.2.0 throws for what doesn't fit: a
/// <see cref="JsonException"/> for a value of the wrong type or a name an enum doesn't have, an
/// <see cref="ArgumentException"/> for a required argument that is missing (<c>McpToolAccessHttpTests</c> pins both).
/// Left to the SDK, both are logged at Error, as if the app were broken, where the caller only mistyped.
/// </para>
/// <para>
/// Only the binder's, though: the same call that binds the arguments runs the tool, so the catch also sees what the
/// tool, its services and its answer throw — a <c>ToDictionary</c> over a repeated key, an answer the serializer can't
/// write. Those are MAM's bugs, not the caller's; told its arguments don't fit, a model would keep changing arguments that
/// were right, and nothing would reach the log. They go on to the SDK, which answers its "An error occurred" and logs
/// them at Error. The binder's are told apart by their stack: its innermost frame outside the runtime's own assemblies
/// (the JSON serializer, the collections) is the binder's, where a tool that ran puts one of its own, a service's or
/// EF Core's first. <c>McpToolAccessHttpTests</c> pins that a tool's own stays the SDK's error.
/// </para>
/// <para>
/// The outermost call filter, so it also sees what reaches the tool past the access gate; a refused call never binds
/// its arguments, so a Read only token's write gets the refusal, whatever its arguments. Tools never throw these
/// exceptions themselves for a failure they expect: those are <see cref="McpToolResults.Error"/> results.
/// </para>
/// </remarks>
public static class McpArgumentErrorFilter
{
    /// <summary>The assembly that binds a tool's arguments: Microsoft.Extensions.AI's <see cref="AIFunctionFactory"/>,
    /// which the SDK's tools are built on.</summary>
    private static readonly Assembly Binder = typeof(AIFunctionFactory).Assembly;

    /// <summary>Turns the binder's exceptions into the sentence.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> CallTool(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException && IsThrownBindingArguments(exception))
            {
                return McpToolResults.Error(McpToolErrors.ArgumentsDontFit(context.Params?.Name ?? "", exception.Message));
            }
        };

    /// <summary>Whether <paramref name="exception"/> came from binding the arguments, before the tool ran: the innermost
    /// frame of its stack outside the runtime's <c>System.*</c> assemblies is the binder's. One the tool threw has a frame
    /// of MAM's, EF Core's or the like there instead.</summary>
    private static bool IsThrownBindingArguments(Exception exception)
    {
        foreach (var frame in new StackTrace(exception).GetFrames())
        {
            // A frame without a type is the runtime's own glue (a reflection stub), as are System.*'s.
            if (frame.GetMethod()?.DeclaringType?.Assembly is not { } assembly || IsRuntime(assembly))
                continue;
            return assembly == Binder;
        }

        return false;
    }

    private static bool IsRuntime(Assembly assembly) =>
        assembly.GetName().Name is { } name && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
}
