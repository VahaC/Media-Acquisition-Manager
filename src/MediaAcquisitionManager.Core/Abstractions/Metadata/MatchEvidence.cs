namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>Why a candidate matched, strongest first (specification §3.1's priorities).</summary>
public enum MatchEvidence
{
    /// <summary>A registry id matched.</summary>
    Identifier = 1,

    /// <summary>The title and the year matched.</summary>
    TitleAndYear = 2,

    /// <summary>Only the title matched.</summary>
    TitleOnly = 3,

    /// <summary>
    /// MAM-12 — not a match: every word of a title sought is in one title or one file path of the work,
    /// which matched in no other way (ADR 0012). Listed for the person to look at, never acted on; a
    /// release is never matched this way.
    /// </summary>
    Words = 4,
}
