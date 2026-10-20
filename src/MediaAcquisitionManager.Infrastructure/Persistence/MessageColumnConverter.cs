using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaAcquisitionManager.Infrastructure.Persistence;

/// <summary>
/// MAM-47 (ADR 0042) — a <see cref="Message"/> property as the TEXT column it was (Adapter): written as its canonical
/// JSON, fitted into the column's 1000 characters (<see cref="MessageFit"/>); read back through
/// <see cref="MessageJson.TryRead"/>.
/// </summary>
/// <remarks>
/// Tolerant Reader / Special Case: text that holds no message — English a rolled-back older image wrote, or a row the
/// startup converter hasn't reached or couldn't parse — reads as <see cref="Message.Legacy"/>, its words shown as they
/// are, instead of failing the query that loaded it. Never throws. EF never passes <c>null</c> through a converter, so a
/// nullable column keeps its nullness and <c>x.Error != null</c> translates as before.
/// </remarks>
internal sealed class MessageColumnConverter() : ValueConverter<Message, string>(
    message => MessageJson.Write(MessageFit.Fit(message, MessageJson.ColumnLength)),
    text => MessageJson.TryRead(text) ?? Message.Legacy(text));
