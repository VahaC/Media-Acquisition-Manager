namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — what removing a work's TMDb link left: the dialog as it now stands.</summary>
/// <param name="Dialog">The work's dialog after the removal.</param>
public sealed record LibraryLinkRemoveResponse(LibraryLinkDialogResponse Dialog);
