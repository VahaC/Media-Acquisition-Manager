namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-20 — a cut or presentation of a film a file's name or a library's version name says it is
/// (<c>EditionLabels</c>): the sets of them part a work's copies — MAM-37 (Vasyl, 2026-10-08: different cuts are not
/// duplicates) — and tell a possible match's files apart (specification §3.3); never a match. An enum, not the text, so
/// the page says it in the user's language (ADR 0001). Written by name.
/// </summary>
public enum MediaEdition
{
    /// <summary>The cut shown in cinemas ("Theatrical Cut", "театральна версія").</summary>
    Theatrical = 1,

    /// <summary>The director's cut ("Director's Cut", "режисерська версія").</summary>
    DirectorsCut = 2,

    /// <summary>A longer cut ("Extended Edition", "розширена версія").</summary>
    Extended = 3,

    /// <summary>A cut released without a rating.</summary>
    Unrated = 4,

    /// <summary>A cut with nothing removed ("Uncut", "без цензури").</summary>
    Uncut = 5,

    /// <summary>A remastered release ("Remastered", "ремастер").</summary>
    Remastered = 6,

    /// <summary>A "Special Edition".</summary>
    SpecialEdition = 7,

    /// <summary>An IMAX presentation (its own aspect ratio in parts).</summary>
    Imax = 8,

    /// <summary>An open-matte frame: more picture at the top and bottom than the cinema showed.</summary>
    OpenMatte = 9,

    /// <summary>A "Final Cut".</summary>
    FinalCut = 10,

    /// <summary>A "Redux" re-edit.</summary>
    Redux = 11,

    /// <summary>An "International Cut" or version.</summary>
    International = 12,

    /// <summary>An "Alternate" or "Alternative" cut or version.</summary>
    Alternate = 13,

    /// <summary>An "Assembly Cut".</summary>
    Assembly = 14,

    /// <summary>A widescreen presentation.</summary>
    Widescreen = 15,

    /// <summary>A full-screen (pan and scan) presentation.</summary>
    FullScreen = 16,
}
