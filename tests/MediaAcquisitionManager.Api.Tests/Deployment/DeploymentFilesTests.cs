using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Api.Tests.Deployment;

// The Docker and configuration files agree with each other and with the app only by convention, and
// a convention someone can forget is pinned here: .env.example documents every variable an
// installation can set, and the container keeps its data on the volume.
public sealed partial class DeploymentFilesTests
{
    // ASP.NET Core's own settings. They are documented by Microsoft and aren't set through MAM_
    // variables in practice, so .env.example doesn't repeat them.
    private static readonly string[] FrameworkSections = ["Logging", "AllowedHosts"];

    [Fact]
    public async Task Env_example_documents_a_variable_for_every_app_setting()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = await AppSettingKeysAsync(cancellationToken);
        var envExample = await File.ReadAllTextAsync(RepositoryPaths.EnvExample, cancellationToken);

        //Act
        var undocumented = settings
            .Select(key => "MAM_" + key.Replace(":", "__", StringComparison.Ordinal))
            .Where(variable => !IsDocumented(envExample, variable))
            .ToList();

        //Assert
        Assert.NotEmpty(settings);
        // On failure: add a commented example line (# MAM_Section__Key=...) with its trade-off.
        Assert.Empty(undocumented);
    }

    [Fact]
    public async Task Env_example_names_only_app_settings_that_exist()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = (await AppSettingKeysAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var envExample = await File.ReadAllTextAsync(RepositoryPaths.EnvExample, cancellationToken);

        //Act
        var unknown = AppVariable().Matches(envExample)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !settings.Contains(name.Replace("__", ":", StringComparison.Ordinal)))
            .ToList();

        //Assert
        Assert.NotEmpty(settings);
        // On failure: a typo in a variable name, or a setting missing from appsettings.json — which
        // lists every setting the app reads, secrets included as null.
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task Env_example_documents_every_variable_the_compose_files_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var envExample = await File.ReadAllTextAsync(RepositoryPaths.EnvExample, cancellationToken);
        var compose = await File.ReadAllTextAsync(RepositoryPaths.DockerCompose, cancellationToken)
            + await File.ReadAllTextAsync(RepositoryPaths.DockerComposeBuild, cancellationToken);

        //Act
        var variables = ComposeVariable().Matches(compose).Select(match => match.Groups["name"].Value).Distinct().ToList();
        var undocumented = variables.Where(variable => !IsDocumented(envExample, variable)).ToList();

        //Assert
        Assert.NotEmpty(variables);
        Assert.Empty(undocumented);
    }

    [Fact]
    public async Task Compose_keeps_the_data_directory_on_the_volume_the_image_uses()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dockerfile = await File.ReadAllTextAsync(RepositoryPaths.Dockerfile, cancellationToken);
        var compose = await File.ReadAllTextAsync(RepositoryPaths.DockerCompose, cancellationToken);

        //Act
        var imageDataDirectory = DockerfileDataDirectory().Match(dockerfile).Groups["path"].Value;
        var composeDataDirectory = ComposeDataDirectory().Match(compose).Groups["path"].Value;
        var volumeTarget = DataVolumeTarget().Match(compose).Groups["path"].Value;

        //Assert
        // A data directory off the volume loses the database and the secrets with the next recreate
        // of the container.
        Assert.Equal("/data", imageDataDirectory);
        Assert.Equal(imageDataDirectory, composeDataDirectory);
        Assert.Equal(imageDataDirectory, volumeTarget);
    }

    [Fact]
    public async Task Compose_publishes_the_port_the_image_listens_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dockerfile = await File.ReadAllTextAsync(RepositoryPaths.Dockerfile, cancellationToken);
        var compose = await File.ReadAllTextAsync(RepositoryPaths.DockerCompose, cancellationToken);

        //Act
        var listening = DockerfileHttpPort().Match(dockerfile).Groups["port"].Value;
        var published = ComposeContainerPort().Match(compose).Groups["port"].Value;

        //Assert
        Assert.NotEmpty(listening);
        Assert.Equal(listening, published);
    }

    // Every leaf of appsettings.json outside the framework's sections, as a configuration key
    // ("Storage:DataDirectory").
    private static async Task<List<string>> AppSettingKeysAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(RepositoryPaths.ApiAppSettings);
        // As lenient as the configuration provider that reads the file, so a comment in it doesn't
        // fail this test while the app starts fine.
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true },
            cancellationToken);

        var keys = new List<string>();
        foreach (var section in document.RootElement.EnumerateObject().Where(section => !FrameworkSections.Contains(section.Name)))
            CollectLeafKeys(section.Value, section.Name, keys);

        return keys;
    }

    private static void CollectLeafKeys(JsonElement element, string path, List<string> keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            keys.Add(path);
            return;
        }

        foreach (var property in element.EnumerateObject())
            CollectLeafKeys(property.Value, $"{path}:{property.Name}", keys);
    }

    // Documented = an assignment line, active or commented out: "NAME=" or "# NAME=".
    private static bool IsDocumented(string envExample, string variable) =>
        Regex.IsMatch(envExample, $@"^#?[ \t]*{Regex.Escape(variable)}=", RegexOptions.Multiline);

    [GeneratedRegex(@"^#?[ \t]*MAM_(?<name>[A-Za-z0-9]+(?:__[A-Za-z0-9]+)+)=", RegexOptions.Multiline)]
    private static partial Regex AppVariable();

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ComposeVariable();

    [GeneratedRegex(@"\bMAM_Storage__DataDirectory=(?<path>\S+)")]
    private static partial Regex DockerfileDataDirectory();

    [GeneratedRegex(@"^[ \t]*MAM_Storage__DataDirectory:[ \t]*(?<path>\S+)", RegexOptions.Multiline)]
    private static partial Regex ComposeDataDirectory();

    [GeneratedRegex(@"^[ \t]*-[ \t]*mam-data:(?<path>[^\s:]+)", RegexOptions.Multiline)]
    private static partial Regex DataVolumeTarget();

    [GeneratedRegex(@"\bASPNETCORE_HTTP_PORTS=(?<port>\d+)")]
    private static partial Regex DockerfileHttpPort();

    // "${APP_BIND_ADDRESS:-127.0.0.1}:${APP_PORT:-8118}:8118" — the container side is the last part.
    [GeneratedRegex(@"^[ \t]*-[ \t]*""[^""\n]*:(?<port>\d+)""", RegexOptions.Multiline)]
    private static partial Regex ComposeContainerPort();
}
