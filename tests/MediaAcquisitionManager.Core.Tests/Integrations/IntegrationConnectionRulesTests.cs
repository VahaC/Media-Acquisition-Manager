using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Integrations;

public sealed class IntegrationConnectionRulesTests
{
    // Spelled out rather than read from IntegrationConnectionRules: they are what the dialog shows.
    private const string NameMissingError = "An integration needs a name.";
    private const string NameTooLongError = "An integration name can be at most 64 characters long.";
    private const string NameControlCharacterError = "An integration name can't contain line breaks or other control characters.";
    private const string BaseUrlMissingError = "An integration needs the address of the service, such as http://192.168.1.10:8096.";
    private const string BaseUrlTooLongError = "The address can be at most 2048 characters long.";
    private const string BaseUrlNotHttpError = "The address must be a full http:// or https:// address, such as http://192.168.1.10:8096.";
    private const string BaseUrlUserInfoError = "Leave the user name and password out of the address; they have fields of their own.";
    private const string BaseUrlQueryError = "The address can't have a query (the part from ?). An API key has a field of its own.";
    private const string BaseUrlFragmentError = "The address can't have a fragment (the part from #).";

    [Theory]
    [InlineData(null, NameMissingError)]
    [InlineData("", NameMissingError)]
    [InlineData("   ", NameMissingError)]
    [InlineData("\t\n", NameMissingError)]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345", NameTooLongError)]
    [InlineData("Media\nserver", NameControlCharacterError)]
    [InlineData("Media\u0000server", NameControlCharacterError)]
    public void ValidateName_is_the_rule(string? name, string expected)
    {
        //Arrange

        //Act
        var error = IntegrationConnectionRules.ValidateName(name);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("Медіасервер")]
    [InlineData("  Jellyfin  ")]
    [InlineData("Jellyfin (living room)")]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901234")]
    public void ValidateName_accepts_a_cyrillic_name(string name)
    {
        //Arrange

        //Act
        var error = IntegrationConnectionRules.ValidateName(name);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(" Jellyfin ", "JELLYFIN")]
    [InlineData("qBittorrent", "QBITTORRENT")]
    [InlineData("Медіасервер", "МЕДІАСЕРВЕР")]
    public void NormalizeName_trims_and_upper_cases(string name, string expected)
    {
        //Arrange
        // Turkish upper-cases "i" to a dotted "İ": a culture-sensitive normalisation would let
        // "jellyfin" and "JELLYFIN" be two integrations on a server with that locale.
        var original = CultureInfo.CurrentCulture;
        string normalized;

        //Act
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            normalized = IntegrationConnectionRules.NormalizeName(name);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        //Assert
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("http://192.168.1.10:8096")]
    [InlineData("https://jellyfin.example.test/")]
    [InlineData("  http://127.0.0.1:9/jellyfin  ")]
    [InlineData("HTTP://HOST:8096")]
    [InlineData("http://192.168.1.195:9696/1/api")]
    [InlineData("http://[::1]:8080")]
    public void ValidateBaseUrl_accepts_http_and_https(string baseUrl)
    {
        //Arrange

        //Act
        var error = IntegrationConnectionRules.ValidateBaseUrl(baseUrl);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null, BaseUrlMissingError)]
    [InlineData("", BaseUrlMissingError)]
    [InlineData("   ", BaseUrlMissingError)]
    [InlineData("ftp://x", BaseUrlNotHttpError)]
    [InlineData("192.168.1.10:8096", BaseUrlNotHttpError)]
    [InlineData("localhost:8096", BaseUrlNotHttpError)]
    [InlineData("/media", BaseUrlNotHttpError)]
    [InlineData("jellyfin", BaseUrlNotHttpError)]
    [InlineData("http://", BaseUrlNotHttpError)]
    [InlineData("http://u:p@h", BaseUrlUserInfoError)]
    [InlineData("https://admin@h:8080/", BaseUrlUserInfoError)]
    [InlineData("http://h/?apikey=1", BaseUrlQueryError)]
    [InlineData("http://h/?", BaseUrlQueryError)]
    [InlineData("http://h/#x", BaseUrlFragmentError)]
    [InlineData("http://h/#", BaseUrlFragmentError)]
    public void ValidateBaseUrl_is_the_rule(string? baseUrl, string expected)
    {
        //Arrange

        //Act
        var error = IntegrationConnectionRules.ValidateBaseUrl(baseUrl);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(2049, BaseUrlTooLongError)]
    [InlineData(2048, null)]
    public void ValidateBaseUrl_of_a_long_address_is_the_rule(int length, string? expected)
    {
        //Arrange
        const string start = "http://h/";
        var baseUrl = start + new string('a', length - start.Length);

        //Act
        var error = IntegrationConnectionRules.ValidateBaseUrl(baseUrl);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("http://h/?apikey=planted-key-0123")]
    [InlineData("http://admin:planted-key-0123@h")]
    [InlineData("ftp://planted-key-0123")]
    [InlineData("http://h/#planted-key-0123")]
    [InlineData("planted-key-0123")]
    public void ValidateBaseUrl_never_repeats_the_address(string baseUrl)
    {
        //Arrange

        //Act
        var error = IntegrationConnectionRules.ValidateBaseUrl(baseUrl);

        //Assert
        Assert.NotNull(error);
        Assert.DoesNotContain("planted-key-0123", error);
    }

    [Theory]
    [InlineData("http://Host:8096/", "http://host:8096")]
    [InlineData("http://h/jellyfin/", "http://h/jellyfin")]
    [InlineData("http://192.168.1.195:9696/1/api", "http://192.168.1.195:9696/1/api")]
    [InlineData("HTTPS://H:443/Jellyfin//", "https://h/Jellyfin")]
    [InlineData("http://h:80", "http://h")]
    [InlineData("  http://h/  ", "http://h")]
    [InlineData("http://@h/x", "http://h/x")]
    [InlineData("http://h/a b/", "http://h/a%20b")]
    public void NormalizeBaseUrl_lowercases_scheme_and_host_keeps_the_path_and_drops_the_trailing_slash(string baseUrl, string expected)
    {
        //Arrange

        //Act
        var normalized = IntegrationConnectionRules.NormalizeBaseUrl(baseUrl);

        //Assert
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("jellyfin", "admin", "Jellyfin doesn't use a user name.")]
    [InlineData("qbittorrent", "ad\nmin", "A user name can't contain line breaks or other control characters.")]
    [InlineData("qbittorrent", "ad\u0007min", "A user name can't contain line breaks or other control characters.")]
    public void ValidateUserName_is_the_rule(string typeId, string userName, string expected)
    {
        //Arrange
        var adapter = Type(typeId);

        //Act
        var error = IntegrationConnectionRules.ValidateUserName(userName, adapter);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(257, "A user name can be at most 256 characters long.")]
    [InlineData(256, null)]
    public void ValidateUserName_of_a_long_name_is_the_rule(int length, string? expected)
    {
        //Arrange
        var userName = new string('a', length);

        //Act
        var error = IntegrationConnectionRules.ValidateUserName(userName, Type("qbittorrent"));

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("qbittorrent", "admin")]
    [InlineData("qbittorrent", "  admin  ")]
    [InlineData("qbittorrent", null)]
    [InlineData("jellyfin", null)]
    [InlineData("jellyfin", "   ")]
    public void ValidateUserName_accepts_a_name_the_type_takes_and_a_blank_one(string typeId, string? userName)
    {
        //Arrange
        var adapter = Type(typeId);

        //Act
        var error = IntegrationConnectionRules.ValidateUserName(userName, adapter);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("jellyfin", false, false, false, "Jellyfin needs an API key.")]
    [InlineData("torznab", false, false, false, "Torznab needs an API key.")]
    [InlineData("signs-in", false, true, false, "Signing-in service needs a user name.")]
    [InlineData("signs-in", true, false, false, "Signing-in service needs a password.")]
    public void ValidateCredentials_requires_what_the_type_requires(string typeId, bool hasUserName, bool hasPassword, bool hasApiKey, string expected)
    {
        //Arrange
        var adapter = Type(typeId);

        //Act
        var error = IntegrationConnectionRules.ValidateCredentials(adapter, hasUserName, hasPassword, hasApiKey);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("jellyfin", true, false, true, "Jellyfin doesn't use a user name.")]
    [InlineData("jellyfin", false, true, true, "Jellyfin doesn't use a password.")]
    [InlineData("torznab", false, true, true, "Torznab doesn't use a password.")]
    [InlineData("qbittorrent", true, true, true, "qBittorrent doesn't use an API key.")]
    [InlineData("jellyfin", true, false, false, "Jellyfin doesn't use a user name.")]
    public void ValidateCredentials_refuses_what_the_type_does_not_use(string typeId, bool hasUserName, bool hasPassword, bool hasApiKey, string expected)
    {
        //Arrange
        var adapter = Type(typeId);

        //Act
        var error = IntegrationConnectionRules.ValidateCredentials(adapter, hasUserName, hasPassword, hasApiKey);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(false, true, "Enter the user name that goes with the password.")]
    [InlineData(true, false, "Enter the password that goes with the user name, or leave both empty if qBittorrent lets this app in without signing in.")]
    public void ValidateCredentials_wants_user_name_and_password_together_when_both_are_optional(bool hasUserName, bool hasPassword, string expected)
    {
        //Arrange
        var adapter = Type("qbittorrent");

        //Act
        var error = IntegrationConnectionRules.ValidateCredentials(adapter, hasUserName, hasPassword, hasApiKey: false);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ValidateCredentials_accepts_neither_when_both_are_optional(bool hasUserName, bool hasPassword)
    {
        //Arrange
        var adapter = Type("qbittorrent");

        //Act
        var error = IntegrationConnectionRules.ValidateCredentials(adapter, hasUserName, hasPassword, hasApiKey: false);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("jellyfin")]
    [InlineData("torznab")]
    public void ValidateCredentials_accepts_a_type_that_has_its_api_key(string typeId)
    {
        //Arrange
        var adapter = Type(typeId);

        //Act
        var error = IntegrationConnectionRules.ValidateCredentials(adapter, hasUserName: false, hasPassword: false, hasApiKey: true);

        //Assert
        Assert.Null(error);
    }

    /// <summary>The real types' descriptors (F.1), and one type that needs both a user name and a
    /// password, which no real type does yet.</summary>
    private static IntegrationAdapterDescriptor Type(string typeId) => typeId switch
    {
        "jellyfin" => new(typeId, "Jellyfin", IntegrationKind.MediaLibrary,
            CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: true),
        "torznab" => new(typeId, "Torznab", IntegrationKind.ReleaseSearch,
            CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false),
        "qbittorrent" => new(typeId, "qBittorrent", IntegrationKind.DownloadClient,
            CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.NotUsed, ReportsFilePaths: true),
        "signs-in" => new(typeId, "Signing-in service", IntegrationKind.DownloadClient,
            CredentialRequirement.Required, CredentialRequirement.Required, CredentialRequirement.NotUsed, ReportsFilePaths: true),
        _ => throw new ArgumentOutOfRangeException(nameof(typeId), typeId, "Not a type these tests know."),
    };
}
