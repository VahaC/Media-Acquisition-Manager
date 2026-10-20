using System.Globalization;
using System.Text;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the IL scan's guard: code with one of each thing <c>NoLiteralSentenceTests</c> (and
/// <c>KeysInUseTests</c>) must flag and one of each it must spare, compiled like the app's. If the scan stops seeing a planted sentence (a compiler change, a
/// broken stack walk), its guard test fails instead of the convention silently passing. Never called.
/// </summary>
public static class ProbeSentences
{
    /// <summary>The planted sentences, each as the scan reads it (a hole written <c>{}</c>).</summary>
    public const string Returned = "The probe planted this sentence for the scan.";

    public const string Interpolated = "The probe couldn't reach {} in time.";

    public const string FirstArm = "The probe chose the first sentence.";

    public const string SecondArm = "The probe chose the second sentence.";

    public const string Logged = "The probe logged this sentence for the scan.";

    public const string Thrown = "The probe threw this sentence for the scan.";

    public const string IntoATextValue = "probe words";

    /// <summary>A sentence kept in a constant nobody reads: flagged in the type that declares it.</summary>
    public const string Unread = "The probe keeps this sentence where nobody reads it.";

    /// <summary>A key named in a constant that the English files haven't: flagged by KeysInUseTests' guard.</summary>
    public const string UnknownKey = "common.probe-no-such-key";

    /// <summary>A key the English files have, named in the IL: spared by KeysInUseTests' guard.</summary>
    public static Message Known() => Message.Of("common.quoted");

    /// <summary>A sentence returned: flagged.</summary>
    public static string Sentence() => Returned;

    /// <summary>A sentence built by interpolation: flagged as one text, not as its pieces.</summary>
    public static string Built(string name) => $"The probe couldn't reach {name} in time.";

    /// <summary>A sentence from either arm of a ternary: both flagged.</summary>
    public static string Chosen(bool first) => first ? FirstArm : SecondArm;

    /// <summary>A sentence logged: spared.</summary>
    public static void Log(ILogger logger) => logger.LogWarning(Logged);

    /// <summary>A sentence of an exception: spared.</summary>
    public static Exception Throw() => new InvalidOperationException(Thrown);

    /// <summary>A literal made a message's value: forbidden whatever it says.</summary>
    public static Message Valued() => Message.Of("common.legacy-text", ("text", IntoATextValue));

    /// <summary>A message printed into a string: forbidden.</summary>
    public static string Printed(Message message) => $"The probe printed {message}";

    /// <summary>A message glued to text (<c>"a" + message</c> calls its <c>ToString()</c>): forbidden.</summary>
    public static string Glued(Message message) => "The probe glued " + message;

    /// <summary>A message formatted into text: forbidden.</summary>
    public static string Formatted(Message message) => string.Format(CultureInfo.InvariantCulture, "{0}", message);

    /// <summary>A message appended to a builder: forbidden.</summary>
    public static StringBuilder Appended(StringBuilder builder, Message message) => builder.Append(message);

    /// <summary>Messages joined into text: forbidden.</summary>
    public static string Joined(IEnumerable<Message> messages) => string.Join(", ", messages);

    /// <summary>A message handed to a log as an argument (the log writes its JSON; <c>texts.English(message)</c> writes
    /// English): forbidden.</summary>
    public static void LoggedMessage(ILogger logger, Message message) => logger.LogWarning("The probe logged {Reason}.", message);

    /// <summary>Stored vendor words made outside the JSON codec and the parser: forbidden.</summary>
    public static VendorText Stored(string text) => VendorText.Stored(text);

    /// <summary>A message compared with a string (<c>message == text</c> compiles through the implicit string
    /// conversion into <see cref="MessageValue"/>'s <c>==</c> and is always false): forbidden.</summary>
    public static bool Compared(Message message, string text) => message == text;

    /// <summary>The same with <c>!=</c>, always true: forbidden.</summary>
    public static bool Differs(Message message, string text) => message != text;

    /// <summary>A record holding a message: the compiler's PrintMembers appends it for the debugger's view, which is
    /// spared — every result record of the conversion holds one.</summary>
    public sealed record Held(Message Reason);
}
