namespace MediaAcquisitionManager.Api.Texts;

/// <summary>MAM-47 (ADR 0042) — words of a template, written as they are.</summary>
/// <param name="Text">The words, spaces and punctuation included.</param>
public sealed record TemplateLiteral(string Text) : TemplatePart;
