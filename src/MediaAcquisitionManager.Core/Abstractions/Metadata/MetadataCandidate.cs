using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>One work a lookup may have meant, with why it matched.</summary>
/// <param name="SourceItemId">The source's own id of the record.</param>
/// <param name="Titles">The titles the source gives it.</param>
/// <param name="Identifiers">The registry ids the source claims for it.</param>
/// <param name="Evidence">Why it matched.</param>
public sealed record MetadataCandidate(
    string SourceItemId,
    IReadOnlyList<ReportedTitle> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers,
    MatchEvidence Evidence);
