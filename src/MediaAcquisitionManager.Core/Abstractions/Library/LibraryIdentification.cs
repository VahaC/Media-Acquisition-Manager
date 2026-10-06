using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — what one <see cref="IMediaLibraryItemIdentifier.IdentifyAsync"/> found and did.</summary>
/// <param name="Outcome">How the identification was decided.</param>
/// <param name="Refusal">Set only when <paramref name="Outcome"/> is <see cref="LibraryIdentifyOutcome.Refused"/>;
/// otherwise <c>null</c>.</param>
/// <param name="Sent">Whether Apply left the app — the caller records every identification that may have changed
/// the item (R10).</param>
/// <param name="IdsBefore">The item's registry ids (IMDb; TMDb as a film's or as a show's; TVDB on a show), well-formed
/// ones only, as read before — read the way the sync reads them, so a scheme means the same everywhere in the app. The
/// audit row's Before.</param>
/// <param name="IdsAfter">The same ids read again after Apply; equal to <paramref name="IdsBefore"/> when nothing was
/// sent; when Apply was sent and the read again never came, the ids the identification meant to leave (the chosen
/// result's, in the same schemes): the audit row records what may have landed.</param>
/// <param name="Sentence">One readable sentence with the call's secrets masked, for every outcome but
/// <see cref="LibraryIdentifyOutcome.Identified"/> and <see cref="LibraryIdentifyOutcome.AlreadyIdentified"/> (<c>null</c>
/// there): what the app stores as the link's error.</param>
public sealed record LibraryIdentification(
    LibraryIdentifyOutcome Outcome,
    LibraryIdentifyRefusal? Refusal,
    bool Sent,
    IReadOnlyList<MediaIdentifier> IdsBefore,
    IReadOnlyList<MediaIdentifier> IdsAfter,
    string? Sentence);
