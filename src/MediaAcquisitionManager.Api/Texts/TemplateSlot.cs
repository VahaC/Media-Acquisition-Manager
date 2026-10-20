namespace MediaAcquisitionManager.Api.Texts;

/// <summary>MAM-47 (ADR 0042) — a placeholder of a template: <c>{{name}}</c> or <c>{{name, hint}}</c>.</summary>
/// <param name="Name">The value it takes from the message.</param>
/// <param name="Hint">What the value is and how the language writes it.</param>
public sealed record TemplateSlot(string Name, SlotHint Hint) : TemplatePart;
