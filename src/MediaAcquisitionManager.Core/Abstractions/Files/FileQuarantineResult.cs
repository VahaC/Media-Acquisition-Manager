using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-26 — the result of a write, or of the read before one, through <see cref="IFileQuarantine"/>. Its own status
/// rather than <see cref="FileInventoryStatus"/>: a write fails in ways a read can't.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Value">What was read or written; <c>null</c> unless <see cref="FileQuarantineStatus.Done"/>.</param>
/// <param name="Error">Why, as a message the Api renders (ADR 0042); <c>null</c> on
/// <see cref="FileQuarantineStatus.Done"/>.</param>
public sealed record FileQuarantineResult<T>(FileQuarantineStatus Status, T? Value, Message? Error) where T : class;
