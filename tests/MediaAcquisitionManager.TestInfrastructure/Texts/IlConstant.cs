using System.Reflection;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — one <c>const string</c>: its text lives in metadata, and the compiler copies it into the IL of each
/// use, so the scan judges it in the type that declares it too — a producer such as <c>DownloadErrors</c> keeps its
/// sentences in constants, and a constant left behind after its uses converted is a dead sentence.
/// </summary>
/// <param name="Field">The constant.</param>
/// <param name="Text">Its value.</param>
public sealed record IlConstant(FieldInfo Field, string Text)
{
    /// <summary>The top-level type that declares <see cref="Field"/>.</summary>
    public Type Owner => IlLiteralScanner.OwnerOf(Field.DeclaringType ?? throw new InvalidOperationException($"{Field.Name} has no declaring type."));

    /// <summary>Where the constant is, for a failure message.</summary>
    public override string ToString() => $"{Owner.FullName}.{Field.Name}: \"{Text}\"";
}
