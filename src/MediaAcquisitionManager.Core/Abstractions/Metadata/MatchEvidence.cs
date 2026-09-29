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
}
