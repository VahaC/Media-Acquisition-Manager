namespace MediaAcquisitionManager.Api.Texts;

/// <summary>MAM-47 (ADR 0042) — what a template's placeholder holds and how it is written, from its hint
/// (<c>{{name, hint}}</c>).</summary>
public enum SlotKind
{
    /// <summary>No hint: a <see cref="Core.Texts.TextValue"/> inserted verbatim. A <see cref="Core.Texts.VendorText"/> fits
    /// too — it is cleaned and masked, so nothing is lost by letting it in; the reverse is refused, since a
    /// <c>vendor</c> slot promises masked words.</summary>
    Text = 1,

    /// <summary><c>vendor</c>: another program's words, a <see cref="Core.Texts.VendorText"/>, inserted verbatim.</summary>
    Vendor = 2,

    /// <summary><c>number</c>: English invariant and ungrouped as today; Ukrainian grouped from 1,000 with a decimal
    /// comma.</summary>
    Number = 3,

    /// <summary><c>number:grouped</c>: English grouped with commas (today's <c>N0</c> byte counts).</summary>
    GroupedNumber = 4,

    /// <summary><c>number:word</c>: English "Two"…"Ten" at a sentence's start, digits otherwise.</summary>
    WordNumber = 5,

    /// <summary><c>size</c>: the exact unit — MiB, KiB or bytes.</summary>
    Size = 6,

    /// <summary><c>duration</c>: the server's clock, <c>1:56:30</c>.</summary>
    Duration = 7,

    /// <summary><c>message</c>: a nested sentence, rendered in the same language.</summary>
    Message = 8,

    /// <summary><c>message:clause</c>: a nested sentence without its one final full stop.</summary>
    Clause = 9,

    /// <summary><c>list:&lt;join&gt;</c>: text items joined the language's way.</summary>
    List = 10,

    /// <summary><c>list:&lt;join&gt;:quoted</c>: text items, each in the language's quotes (<c>common.quoted</c>).</summary>
    QuotedList = 11,

    /// <summary><c>list:&lt;join&gt;:message</c>: nested sentences, joined.</summary>
    MessageList = 12,

    /// <summary>
    /// <c>name</c>: what something is called — a <see cref="Core.Texts.TextValue"/> inserted verbatim ("Alien (1979)"),
    /// or, for a thing that has no name, a label: a nested message written in the same language
    /// (<c>replacements.pairing.work.untitled</c>). MAM-47 WP12 — a slot holds data or words of ours, never English
    /// words passed as data; a plain text slot still refuses a message.
    /// </summary>
    Name = 13,

    /// <summary><c>list:&lt;join&gt;:name</c>: names (<see cref="Name"/>), joined.</summary>
    NameList = 14,
}
