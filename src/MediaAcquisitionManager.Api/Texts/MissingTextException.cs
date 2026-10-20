namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a server sentence that can't be written as its message asks: a key with no template, a value the
/// template names but the message lacks, a value no template of the key names, or a value whose kind doesn't fit its
/// placeholder's hint. Thrown only by the strict renderer (every test host), so the test that reached the broken
/// sentence fails there; production renders the nearest readable fallback and logs a Warning instead.
/// </summary>
public sealed class MissingTextException(string message) : Exception(message);
