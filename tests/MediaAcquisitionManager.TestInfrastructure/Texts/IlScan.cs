namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>MAM-47 (ADR 0042) — what <see cref="IlLiteralScanner"/> read.</summary>
/// <param name="Types">Every type scanned, nested and compiler-generated ones included.</param>
/// <param name="MethodCount">Methods and constructors whose IL was read.</param>
/// <param name="Uses">Every text built from literals and where it went.</param>
/// <param name="Strings">Every string literal.</param>
/// <param name="Calls">Every call and constructor call.</param>
/// <param name="Printed">Every call that writes a message into a string (<c>ToString()</c>, <c>string.Concat</c>,
/// <c>string.Format</c>, <c>string.Join</c>, a <c>StringBuilder</c>'s appending), outside the compiler's own record
/// printers.</param>
/// <param name="Constants">Every <c>const string</c> of the types scanned.</param>
/// <param name="Unresolved">Tokens the scan couldn't resolve, so it lost track of the stack there; the convention tests
/// expect none.</param>
public sealed record IlScan(
    IReadOnlySet<Type> Types,
    int MethodCount,
    IReadOnlyList<LiteralUse> Uses,
    IReadOnlyList<IlString> Strings,
    IReadOnlyList<IlCall> Calls,
    IReadOnlyList<IlCall> Printed,
    IReadOnlyList<IlConstant> Constants,
    IReadOnlyList<string> Unresolved);
