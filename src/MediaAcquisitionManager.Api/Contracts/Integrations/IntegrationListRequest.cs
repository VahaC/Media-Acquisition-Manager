namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the integration list: a search, a kind and a page, all applied by
/// the server.</summary>
/// <remarks>
/// MAM-7 — <paramref name="Kind"/> is a string, not <c>IntegrationKind</c>: an enum-typed query
/// parameter would answer a bad value with a validation body, not the <c>{ error }</c> sentence the
/// page shows.
/// </remarks>
/// <param name="Search">Part of an integration's name, in any letter case; blank lists every one.</param>
/// <param name="Kind"><c>MediaLibrary</c>, <c>ReleaseSearch</c> or <c>DownloadClient</c>, in any letter
/// case; blank lists every kind.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Integrations per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record IntegrationListRequest(string? Search = null, string? Kind = null, int Page = 1, int PageSize = 25);
