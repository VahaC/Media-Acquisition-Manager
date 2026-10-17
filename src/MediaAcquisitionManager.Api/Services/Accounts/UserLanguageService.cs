using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <inheritdoc cref="IUserLanguageService"/>
public sealed class UserLanguageService(ApplicationDbContext db, ILogger<UserLanguageService> logger) : IUserLanguageService
{
    /// <summary>
    /// The sentence a body that can't be read as the language gets — not JSON, empty, without the <c>language</c>
    /// property, or a value that isn't text — instead of the validation body MVC would send: it names the field and what
    /// it takes.
    /// </summary>
    public const string UnreadableBodyError = "Send the language as JSON: language en, uk, or null to follow the browser.";

    /// <summary>How much of an unknown value the sentence repeats: every real code fits, and a pasted page of text
    /// shouldn't come back whole.</summary>
    private const int EchoLength = 32;

    public async Task<UserLanguageSaveResult> SaveAsync(UserLanguageRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Compared exactly, as the web app sends it: "UK" or "uk-UA" is no locale it has, and storing one would leave the
        // app unable to say which language the account means.
        if (request.Language is { } language && !UiLanguages.IsSupported(language))
            return UserLanguageSaveResult.Invalid($"“{Echo(language)}” is not a language of the app. Use en, uk, or null to follow the browser.");

        // One statement: the column is the user's alone, and a save of the same value again changes nothing more.
        var saved = await db.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Language, request.Language), cancellationToken);
        if (saved == 0)
            return UserLanguageSaveResult.UserGone;

        logger.LogInformation("User {UserId} saved the language {Language} for the web app.", userId, request.Language ?? "(as in the browser)");
        return UserLanguageSaveResult.Saved(new UserLanguageDto(request.Language));
    }

    private static string Echo(string value) => value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
