using System.Reflection;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>MAM-47 (ADR 0042) — one string literal (<c>ldstr</c>) of a method's IL, exactly as written.</summary>
public sealed record IlString(MethodBase Method, string Text);
