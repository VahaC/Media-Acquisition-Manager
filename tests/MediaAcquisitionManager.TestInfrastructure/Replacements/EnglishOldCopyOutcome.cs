using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-47 (ADR 0042) — a <see cref="ReplacementOldCopyOutcome"/> with its problem as a reader sees it in English, so the
/// pairing's tests keep comparing whole outcomes against today's sentences now that the problem is a message.
/// </summary>
/// <param name="OldCopyId">The copy's row id.</param>
/// <param name="PairedNewFileId">The new file it is paired with.</param>
/// <param name="Problem">Its problem in English (strict, <see cref="Texts.TestTexts.En"/>).</param>
/// <param name="OnlyDropSettles">Whether only a drop settles the problem.</param>
public sealed record EnglishOldCopyOutcome(Guid OldCopyId, Guid? PairedNewFileId, string? Problem, bool OnlyDropSettles = false);
