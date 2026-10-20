using System.Reflection;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — where code compares a message with a string: <see cref="MessageValue"/>'s own <c>==</c> or
/// <c>!=</c> called from outside it. A <c>Message</c> meets a <c>string</c> only there, through the implicit string
/// conversion, so <c>reason == SomeReason</c> — or a test's <c>row.Error == "…"</c> — still compiles after a member's type
/// changed from <c>string</c> to <c>Message</c>, and is always false (unequal: a test that meant to find a row finds
/// none). Two messages compare through <c>Message</c>'s own operator, which this spares. One rule for the source
/// assemblies and every test assembly, each scanned by a convention test of its own project.
/// </summary>
public static class MessageComparisons
{
    /// <summary>Whether <paramref name="call"/> compares a message with a string.</summary>
    public static bool WithAString(IlCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Callee is MethodInfo { Name: "op_Equality" or "op_Inequality" } method
            && method.DeclaringType == typeof(MessageValue)
            && call.Owner != typeof(MessageValue);
    }

    /// <summary>Every comparison of a message with a string in <paramref name="scans"/>, outside the
    /// <paramref name="spared"/> types, as "Owner.Method → Callee", once each.</summary>
    public static IReadOnlyList<string> WithAStringIn(IEnumerable<IlScan> scans, params Type[] spared)
    {
        ArgumentNullException.ThrowIfNull(scans);
        return
        [
            .. scans
                .SelectMany(scan => scan.Calls)
                .Where(call => WithAString(call) && !spared.Contains(call.Owner))
                .Select(call => $"{call.Owner.FullName}.{call.Method.Name} → {call.Callee.DeclaringType?.Name}.{call.Callee.Name}")
                .Distinct(),
        ];
    }
}
