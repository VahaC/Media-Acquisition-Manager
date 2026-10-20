using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-47 (ADR 0042) — a <see cref="ReplacementNewFileOutcome"/> with its problem as a reader sees it in English, so the
/// pairing's tests keep comparing whole outcomes against today's sentences now that the problem is a message.
/// </summary>
/// <param name="NewFileId">The new file's row id.</param>
/// <param name="WorkId">The matched work the library's ids identify it as.</param>
/// <param name="Problem">Its problem in English (strict, <see cref="Texts.TestTexts.En"/>).</param>
public sealed record EnglishNewFileOutcome(Guid NewFileId, Guid? WorkId, string? Problem);
