using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IReplacementMapper"/>
/// <remarks>MAM-47 (ADR 0042) — the destination's problem, each client's error and each work's confirmation are messages,
/// rendered in the request's language with <c>texts</c>.</remarks>
public sealed class ReplacementMapper(IRequestTexts texts) : IReplacementMapper
{
    public ReplacementOptionsResponse ToResponse(
        ReplacementUnavailableReason? unavailable,
        Message? destinationProblem,
        IReadOnlyList<MediaEdition> releaseEditions,
        IReadOnlyList<ReplacementCandidateWork> works,
        ReplacementHoldings holdings) =>
        new(
            unavailable,
            texts.Render(destinationProblem),
            releaseEditions,
            [.. works.Select(work => ToDto(work, holdings))],
            [.. holdings.Clients.Select(client => new ReplacementClientCheckDto(client.ClientName, client.Status, texts.Render(client.Error), client.UnplacedTorrents))]);

    private ReplacementWorkDto ToDto(ReplacementCandidateWork work, ReplacementHoldings holdings) =>
        new(
            work.WorkId,
            work.Kind,
            work.Title,
            work.Year,
            work.Match.Evidence,
            work.Match.ConflictingSchemes,
            work.NeedsConfirmation,
            texts.Render(work.ConfirmationText),
            work.LibraryNames,
            work.Unavailable,
            [.. work.Copies.Select(copy => ToDto(copy, work, holdings))],
            [.. work.Episodes.Select(episode => new ReplacementEpisodeDto(
                episode.WorkId,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                episode.Title,
                [.. episode.Copies.Select(copy => ToDto(copy, work, holdings))]))]);

    // ADR 0031 — a hold is said of a copy the owner may tick, the sentence he reads before he schedules; never a reason
    // it can't be ticked.
    private static ReplacementCopyDto ToDto(ReplacementCandidateCopy copy, ReplacementCandidateWork work, ReplacementHoldings holdings) =>
        new(
            copy.File,
            copy.Version.Id,
            copy.Version.EditionLabel,
            copy.Editions,
            copy.DynamicRanges,
            work.Unavailable is null && copy.OtherNames,
            copy.Unavailable,
            work.Unavailable is null && copy is { Unavailable: null, AppPath: { } appPath } && holdings.Holds.TryGetValue(appPath, out var holds)
                ? [.. holds.Select(hold => new ReplacementHoldDto(hold.ClientName, hold.TorrentId, hold.TorrentName, hold.SecondName))]
                : []);
}
