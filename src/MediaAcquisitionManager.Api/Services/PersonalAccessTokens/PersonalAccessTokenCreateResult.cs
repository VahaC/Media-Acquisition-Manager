using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <summary>MAM-5 — the outcome of <see cref="IPersonalAccessTokenService.CreateAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Created">The token and its secret when <see cref="PersonalAccessTokenCreateStatus.Created"/>.</param>
/// <param name="Error">Why, as a message (ADR 0042), when <see cref="PersonalAccessTokenCreateStatus.Invalid"/>.</param>
public sealed record PersonalAccessTokenCreateResult(PersonalAccessTokenCreateStatus Status, CreatedPersonalAccessTokenResponse? Created, Message? Error)
{
    public static PersonalAccessTokenCreateResult Succeeded(CreatedPersonalAccessTokenResponse created) =>
        new(PersonalAccessTokenCreateStatus.Created, created, null);

    public static PersonalAccessTokenCreateResult Invalid(Message error) => new(PersonalAccessTokenCreateStatus.Invalid, null, error);

    /// <summary>The caller's session ended after it let the request in; the caller answers with its own 401.</summary>
    public static PersonalAccessTokenCreateResult SessionEnded { get; } = new(PersonalAccessTokenCreateStatus.SessionEnded, null, null);
}
