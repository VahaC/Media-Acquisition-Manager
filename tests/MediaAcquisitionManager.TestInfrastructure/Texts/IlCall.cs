using System.Reflection;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>MAM-47 (ADR 0042) — one call or constructor call of a method's IL, with generic arguments resolved.</summary>
public sealed record IlCall(MethodBase Method, MethodBase Callee)
{
    /// <summary>The top-level type that declares <see cref="Method"/>.</summary>
    public Type Owner => IlLiteralScanner.OwnerOf(Method);
}
