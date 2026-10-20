using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — how far a write over a replacement's names got, as one clause: "1 of 3 names is in the quarantine", "2 of 3
/// names are back where they were", "1 of 1 name is deleted". The mover's stops and its recovery of a cut write, and the
/// owner's Restore and Delete for good, all say it through here, so the noun agrees with the total and the verb — with its
/// pronoun — with the count the same way in every sentence; and a write that got nowhere says no count at all, rather than
/// "0 of 3 names are".
/// </summary>
/// <remarks>
/// MAM-47 (ADR 0042) — a message of the <c>replacements.quarantine.tally</c> keys, which a sentence's <c>-tally</c> key
/// nests: the names written are its count, which picks the verb's form, and the total is a counted noun of its own, nested
/// (design §2.2: one plural driver per key), so each language agrees both as its grammar wants.
/// </remarks>
internal static class ReplacementNameTally
{
    /// <summary>"<paramref name="count"/> of <paramref name="total"/> names are in the quarantine", or <c>null</c> when
    /// none is.</summary>
    public static Message? InQuarantine(int count, int total) =>
        count == 0 ? null : Message.Of("replacements.quarantine.tally.in-quarantine", ("count", count), ("names", Names(total)));

    /// <summary>"<paramref name="count"/> of <paramref name="total"/> names are back where they were", or <c>null</c>
    /// when none is.</summary>
    public static Message? BackWhereTheyWere(int count, int total) =>
        count == 0 ? null : Message.Of("replacements.quarantine.tally.back", ("count", count), ("names", Names(total)));

    /// <summary>"<paramref name="count"/> of <paramref name="total"/> names are deleted", or <c>null</c> when none
    /// is.</summary>
    public static Message? Deleted(int count, int total) =>
        count == 0 ? null : Message.Of("replacements.quarantine.tally.deleted", ("count", count), ("names", Names(total)));

    // "3 names": the total, a counted noun of its own.
    private static Message Names(int total) => Message.Of("replacements.quarantine.tally.names", ("count", total));
}
