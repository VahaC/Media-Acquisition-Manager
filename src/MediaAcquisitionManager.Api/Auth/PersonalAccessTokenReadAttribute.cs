namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — a POST that only reads, open to every personal access token like a GET: the two searches
/// (<c>POST api/searches</c>, <c>POST api/releases/search</c>), which take a query too long for a URL and store nothing
/// but the releases they found for the caller. A token has to search before it can send (ADR 0040).
/// </summary>
/// <remarks>Endpoint metadata, read by <see cref="PersonalAccessTokenAccessHandler"/>; every other write refuses a token
/// unless it carries <see cref="PersonalAccessTokenWriteAttribute"/>.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PersonalAccessTokenReadAttribute : Attribute;
