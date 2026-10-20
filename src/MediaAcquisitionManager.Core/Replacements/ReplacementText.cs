namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — the names the verification's sentences quote of what a service or a disk named (ADR 0035): a file's own name
/// out of its path.
/// </summary>
/// <remarks>MAM-47 (ADR 0042) — the reasons are messages now, quoted by their templates over
/// <see cref="Texts.MessageText.Clean"/> (which cleans and cuts as the quoting here did) and listed by their <c>list</c>
/// hints: the quoting and the listing are gone, the file's name stays.</remarks>
public static class ReplacementText
{
    /// <summary>The file's own name: the part of <paramref name="path"/> after its last <c>/</c> or <c>\</c>.</summary>
    public static string FileNameOf(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
}
