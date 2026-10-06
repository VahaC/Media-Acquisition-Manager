namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a library work's TMDb dialog: the work, its items with their links and the library's state, and the
/// suggestions to pick from.</summary>
/// <param name="Work">The work as the Library page shows it, its link included.</param>
/// <param name="Items">Each library item the work holds (a work can hold several: the two "Чужі"), with its link.</param>
/// <param name="CatalogConnected">Whether a metadata catalog is connected: without one nothing can be linked.</param>
/// <param name="Suggestions">The TMDb candidates prepared for the work, best first, at most five, the confident one
/// always among them; none while none were prepared.</param>
/// <param name="Search">Where the preparation of the work's suggestions stands.</param>
/// <param name="SearchTitle">The title the free search starts from: the work's title cleaned the way the preparation's
/// queries are ("Landing Stripling" of "120. Landing Stripling"); <c>null</c> when nothing of a title is left.</param>
/// <param name="SearchYear">The year the free search starts from: the one that title names in brackets (the first of a
/// span), else the work's; <c>null</c> when none says.</param>
public sealed record LibraryLinkDialogResponse(
    LibraryWorkDto Work,
    IReadOnlyList<LibraryLinkItemDto> Items,
    bool CatalogConnected,
    IReadOnlyList<LinkCandidateDto> Suggestions,
    LibraryLinkSearchStateDto Search,
    string? SearchTitle,
    int? SearchYear);
