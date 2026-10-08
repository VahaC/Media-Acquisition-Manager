namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>MAM-38 — the four kinds of bencoded value (BEP 3).</summary>
public enum BencodeKind
{
    /// <summary><c>i42e</c>.</summary>
    Integer = 1,

    /// <summary><c>4:spam</c>: bytes, not necessarily text.</summary>
    Bytes = 2,

    /// <summary><c>l…e</c>.</summary>
    List = 3,

    /// <summary><c>d…e</c>, its keys byte strings.</summary>
    Dictionary = 4,
}
