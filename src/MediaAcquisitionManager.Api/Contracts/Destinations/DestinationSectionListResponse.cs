namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The sections of every release source, each with the destination it is mapped to (MAM-32).</summary>
/// <remarks>
/// <para>
/// MAM-32 — a live call, so it answers 200 whatever the sources said (ADR 0005): a source that couldn't be
/// asked answers its sentence beside the others' sections.
/// </para>
/// <para>
/// Whole, neither searched nor paged: a destination's save replaces its sections with the ones it sends,
/// so the dialog must see every one mapped, and a page would ask every source again. One source lists a
/// hundred and some, once.
/// </para>
/// </remarks>
/// <param name="Sources">Every release search integration by name, switched off ones too — not asked, but
/// with the sections mapped of them, which a save would otherwise drop unseen.</param>
public sealed record DestinationSectionListResponse(IReadOnlyList<DestinationSectionSourceDto> Sources);
