using System.Reflection;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — where text written in code ends up: the text as the method builds it (string literals joined the
/// way the method joins them, each hole of an interpolation or a concatenation written <c>{}</c>), and what takes it.
/// </summary>
/// <param name="Method">The method whose IL holds the literal (a lambda's or an iterator's own method included).</param>
/// <param name="Text">The text, <c>"Couldn't reach {} in time."</c> for <c>$"Couldn't reach {name} in time."</c>.</param>
/// <param name="Callee">The method or constructor that takes it; <c>null</c> when it is stored or returned.</param>
/// <param name="Consumer">What takes it, readable: <c>LoggerExtensions.LogWarning</c>, <c>new InvalidOperationException</c>,
/// <c>field Type.Name</c>, <c>return</c>, <c>array element</c>, <c>indirect store</c>.</param>
public sealed record LiteralUse(MethodBase Method, string Text, MethodBase? Callee, string Consumer)
{
    /// <summary>The top-level type that declares <see cref="Method"/>: a lambda's or a state machine's outer type.</summary>
    public Type Owner => IlLiteralScanner.OwnerOf(Method);

    /// <summary>Where the text is, for a failure message.</summary>
    public override string ToString() => $"{Owner.FullName}.{Method.Name}: \"{Text}\" → {Consumer}";
}
