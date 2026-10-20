using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — every contract field that carries a server sentence (the frontend scout's list of 37, plus the
/// release search's per-source error, the error body's own and the MCP Download options' held reason), and the fields
/// whose names sound like a sentence but hold another product's data. In the pseudo pass the factory's JSON options
/// read a listed field as a sentence — null, or one wrapped message, stripped to its English — and refuse a mark in any
/// other string, so a sentence field missing here fails where it is read.
/// </summary>
/// <remarks>
/// <c>PseudoSentenceFieldsTests</c> keeps both lists complete: every contract property named like a sentence must be in
/// one of them, so a new field can't slip past the pass. A field is named by its declaring type and property, as
/// <c>nameof</c> spells it, so a rename fails the build.
/// </remarks>
public static class PseudoSentenceFields
{
    /// <summary>The fields the server writes a sentence into.</summary>
    public static IReadOnlyList<(Type Type, string Property)> Sentences { get; } =
    [
        (typeof(ApiErrorResponse), nameof(ApiErrorResponse.Error)),
        (typeof(DownloadJobDto), nameof(DownloadJobDto.Error)),
        (typeof(DownloadJobDto), nameof(DownloadJobDto.BlockReason)),
        (typeof(DownloadOverwriteCheckResponse), nameof(DownloadOverwriteCheckResponse.ClientError)),
        (typeof(CopyRemovalVerdictDto), nameof(CopyRemovalVerdictDto.Reason)),
        (typeof(CopyRemovalDto), nameof(CopyRemovalDto.Reason)),
        (typeof(DestinationCheckDto), nameof(DestinationCheckDto.Problem)),
        (typeof(DestinationSectionSourceDto), nameof(DestinationSectionSourceDto.Error)),
        (typeof(UnplacedLibraryFolderDto), nameof(UnplacedLibraryFolderDto.Problem)),
        (typeof(ClientDestinationListResponse), nameof(ClientDestinationListResponse.Error)),
        (typeof(HealthCheckDto), nameof(HealthCheckDto.Description)),
        (typeof(ConnectionTestResponse), nameof(ConnectionTestResponse.Message)),
        (typeof(LibraryWorkLinkDto), nameof(LibraryWorkLinkDto.WriteError)),
        (typeof(LibraryLinkItemDto), nameof(LibraryLinkItemDto.CannotWriteReason)),
        (typeof(LibraryLinkItemDto), nameof(LibraryLinkItemDto.WriteError)),
        (typeof(LibraryLinkRowDto), nameof(LibraryLinkRowDto.WriteError)),
        (typeof(LibraryLinkBatchItemDto), nameof(LibraryLinkBatchItemDto.Reason)),
        (typeof(LibraryLinkSuggestionRunDto), nameof(LibraryLinkSuggestionRunDto.Error)),
        (typeof(LibraryFileDto), nameof(LibraryFileDto.PathProblem)),
        (typeof(LibraryFileInspectionDto), nameof(LibraryFileInspectionDto.Problem)),
        (typeof(LibrarySyncRunDto), nameof(LibrarySyncRunDto.Error)),
        (typeof(LibrarySourceDto), nameof(LibrarySourceDto.PathProblem)),
        (typeof(LibrarySourceInspectionDto), nameof(LibrarySourceInspectionDto.Problem)),
        (typeof(FileInspectionRunDto), nameof(FileInspectionRunDto.Error)),
        (typeof(FileHashingDto), nameof(FileHashingDto.Problem)),
        (typeof(FolderScanRunDto), nameof(FolderScanRunDto.Problem)),
        (typeof(FolderScanRunDto), nameof(FolderScanRunDto.Error)),
        (typeof(RecommendationRunDto), nameof(RecommendationRunDto.Error)),
        (typeof(RecommendationSourceFailureDto), nameof(RecommendationSourceFailureDto.Error)),
        (typeof(ReplacementWorkDto), nameof(ReplacementWorkDto.ConfirmationText)),
        (typeof(ReplacementClientCheckDto), nameof(ReplacementClientCheckDto.Error)),
        (typeof(ReplacementOptionsResponse), nameof(ReplacementOptionsResponse.DestinationProblem)),
        (typeof(DownloadJobReplacementDto), nameof(DownloadJobReplacementDto.Reason)),
        (typeof(ReplacementDetailsDto), nameof(ReplacementDetailsDto.Reason)),
        (typeof(ReplacementNameDto), nameof(ReplacementNameDto.Problem)),
        (typeof(ReplacementJobCopyDto), nameof(ReplacementJobCopyDto.Problem)),
        (typeof(ReplacementJobNewFileDto), nameof(ReplacementJobNewFileDto.Problem)),
        (typeof(SearchSourceDto), nameof(SearchSourceDto.Error)),
        (typeof(ReleaseSourceResultDto), nameof(ReleaseSourceResultDto.Error)),
        (typeof(DownloadOptionsToolResult), nameof(DownloadOptionsToolResult.SendHeldReason)),
    ];

    /// <summary>Fields named like a sentence that hold data, shown as they are: a tracker's topic title, the catalog's
    /// overview, a filter's value.</summary>
    public static IReadOnlyList<(Type Type, string Property)> NotSentences { get; } =
    [
        (typeof(FoundReleaseDto), nameof(FoundReleaseDto.Description)),
        (typeof(SearchReleaseDto), nameof(SearchReleaseDto.Description)),
        (typeof(LinkCandidateDto), nameof(LinkCandidateDto.Overview)),
        (typeof(RecommendationOverviewResponse), nameof(RecommendationOverviewResponse.Overview)),
        (typeof(RecommendationListRequest), nameof(RecommendationListRequest.Reason)),
    ];

    private static readonly HashSet<(Type Type, string Property)> SentenceSet = [.. Sentences];

    /// <summary>Whether <paramref name="property"/> of <paramref name="type"/> carries a server sentence.</summary>
    public static bool IsSentence(Type type, string property) => SentenceSet.Contains((type, property));
}
