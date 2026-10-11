using System.Globalization;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — how far a write over a replacement's names got, as one clause: "1 of 3 names is in the quarantine", "2 of 3
/// names are back where they were", "1 of 1 name is deleted". The mover's stops and its recovery of a cut write, and the
/// owner's Restore and Delete for good, all say it through here, so the noun agrees with the total and the verb — with its
/// pronoun — with the count the same way in every sentence; and a write that got nowhere says no count at all, rather than
/// "0 of 3 names are".
/// </summary>
internal static class ReplacementNameTally
{
    /// <summary>"<paramref name="count"/> of <paramref name="total"/> names are in the quarantine", or <c>null</c> when
    /// none is.</summary>
    public static string? InQuarantine(int count, int total) => Of(count, total, "is in the quarantine", "are in the quarantine");

    /// <summary>"<paramref name="count"/> of <paramref name="total"/> names are back where they were", or <c>null</c>
    /// when none is.</summary>
    public static string? BackWhereTheyWere(int count, int total) => Of(count, total, "is back where it was", "are back where they were");

    /// <summary>"<paramref name="count"/> of <paramref name="total"/> names are deleted", or <c>null</c> when none
    /// is.</summary>
    public static string? Deleted(int count, int total) => Of(count, total, "is deleted", "are deleted");

    /// <summary>
    /// <paramref name="lead"/> — a sentence without its full stop — ended by <paramref name="clause"/> after a colon,
    /// "Replacement was switched off while the old copies were moving: 1 of 4 names is in the quarantine.", or by a full
    /// stop alone when there is none.
    /// </summary>
    public static string AfterColon(string lead, string? clause) => clause is null ? $"{lead}." : $"{lead}: {clause}.";

    /// <summary>
    /// <paramref name="sentence"/> — ended already, often with a sentence of the disk's own — followed by
    /// <paramref name="clause"/> as a sentence of its own, or alone when there is none.
    /// </summary>
    public static string AfterSentence(string sentence, string? clause) => clause is null ? sentence : $"{sentence} {clause}.";

    private static string? Of(int count, int total, string one, string many) =>
        count == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{count} of {total} {(total == 1 ? "name" : "names")} {(count == 1 ? one : many)}");
}
