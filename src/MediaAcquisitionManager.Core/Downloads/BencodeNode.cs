namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — one bencoded value as <see cref="BencodeParser"/> read it. Its bytes are slices of the encoded
/// file, never copies: a .torrent's <c>pieces</c> and piece layers are megabytes.
/// </summary>
/// <param name="Kind">Which kind it is.</param>
/// <param name="Encoded">The value exactly as it was encoded, its type letters and lengths included.</param>
/// <param name="Integer">For <see cref="BencodeKind.Integer"/>, its value.</param>
/// <param name="Bytes">For <see cref="BencodeKind.Bytes"/>, its bytes.</param>
/// <param name="Items">For <see cref="BencodeKind.List"/>, its items; for <see cref="BencodeKind.Dictionary"/>,
/// its values, in the order of <paramref name="Keys"/>.</param>
/// <param name="Keys">For <see cref="BencodeKind.Dictionary"/>, its keys, in the order they were encoded.</param>
public sealed record BencodeNode(
    BencodeKind Kind,
    ReadOnlyMemory<byte> Encoded,
    long Integer,
    ReadOnlyMemory<byte> Bytes,
    IReadOnlyList<BencodeNode> Items,
    IReadOnlyList<ReadOnlyMemory<byte>> Keys)
{
    /// <summary>The value under <paramref name="key"/> when this is a dictionary that has one; else <c>null</c>.
    /// The first of two equal keys, which bencode never has.</summary>
    public BencodeNode? Find(ReadOnlySpan<byte> key)
    {
        if (Kind != BencodeKind.Dictionary)
            return null;
        for (var index = 0; index < Keys.Count; index++)
        {
            if (Keys[index].Span.SequenceEqual(key))
                return Items[index];
        }

        return null;
    }

    /// <summary>The integer under <paramref name="key"/>, when this is a dictionary that has one.</summary>
    public long? IntegerOf(ReadOnlySpan<byte> key) => Find(key) is { Kind: BencodeKind.Integer } value ? value.Integer : null;

    /// <summary>The bytes under <paramref name="key"/>, when this is a dictionary that has them.</summary>
    /// <remarks>The <c>null</c> is cast on purpose: bare, it would bind through the implicit conversion from a
    /// <c>byte[]</c> and come back as empty bytes, not as none.</remarks>
    public ReadOnlyMemory<byte>? BytesOf(ReadOnlySpan<byte> key) =>
        Find(key) is { Kind: BencodeKind.Bytes } value ? value.Bytes : (ReadOnlyMemory<byte>?)null;
}
