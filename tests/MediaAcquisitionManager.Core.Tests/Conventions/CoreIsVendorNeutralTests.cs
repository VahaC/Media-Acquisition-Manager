using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Conventions;

/// <summary>
/// ADR 0005 — the core knows a service only through a registered adapter type: no product name in any
/// namespace, type or member of the core, so a new product never needs a core change and nothing in
/// the core quietly depends on one product's shape. Doc comments may name products; names may not.
/// </summary>
public sealed partial class CoreIsVendorNeutralTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Distinctive enough to match anywhere in a lower-cased name.</summary>
    private static readonly string[] ProductNames =
    [
        "jellyfin", "torznab", "newznab", "prowlarr", "jackett", "toloka", "hurtom",
        "qbittorrent", "sabnzbd", "nzbget", "rtorrent", "utorrent", "radarr", "sonarr",
        // MAM-9: the media inspector is a tool behind IFileInventory, named in Infrastructure only (ADR 0005).
        "ffprobe", "ffmpeg", "mediainfo",
    ];

    /// <summary>
    /// Words that hide inside ordinary ones ("Complex" holds "plex"), so they match only whole words of
    /// a name — or a run of them, so "QBitClient" (Q + Bit) still counts.
    /// </summary>
    private static readonly string[] ProductWords = ["plex", "emby", "kodi", "deluge", "transmission", "qbit"];

    private static readonly Assembly Core = typeof(BaseEntity).Assembly;

    [Fact]
    public void Core_references_only_the_base_class_library()
    {
        //Arrange
        var references = Core.GetReferencedAssemblies();

        //Act
        var others = references
            .Select(reference => reference.Name ?? "")
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name is not ("netstandard" or "mscorlib"))
            .ToList();

        //Assert
        Assert.Empty(others);
    }

    [Fact]
    public void No_core_type_member_or_namespace_is_named_after_a_product()
    {
        //Arrange
        var scan = Scan();

        //Act
        var offenders = scan.Names
            .Where(name => NamesAProduct(name.Name))
            .Select(name => $"{name.Name} (in {name.Where})")
            .Distinct()
            .ToList();

        //Assert
        // Joined rather than Assert.Empty, which cuts each item short before the name it found.
        Assert.True(offenders.Count == 0, $"Core names that name a product: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_covers_the_core()
    {
        //Arrange
        Type[] mustBeScanned =
        [
            typeof(IIntegrationAdapter), typeof(IMediaLibraryProvider), typeof(IReleaseSearchProvider),
            typeof(IDownloadClient), typeof(IFileInventory), typeof(IMetadataResolver),
            typeof(IntegrationConnectionEntity), typeof(PathMapper),
            // MAM-26 — the write contract beside the inventory (ADR 0030).
            typeof(IFileQuarantine),
        ];

        //Act
        var scan = Scan();

        //Assert
        Assert.True(scan.Types.Count >= 60, $"Only {scan.Types.Count} types were scanned.");
        Assert.True(scan.MemberCount >= 300, $"Only {scan.MemberCount} members were scanned.");
        Assert.Subset(scan.Types, mustBeScanned.ToHashSet());
    }

    [Theory]
    [InlineData("JellyfinItem", true)]
    [InlineData("QBittorrentJob", true)]
    [InlineData("QBitClient", true)]
    [InlineData("PlexToken", true)]
    [InlineData("TransmissionRate", true)]
    [InlineData("embyServerId", true)]
    [InlineData("ComplexValue", false)]
    [InlineData("TorrentFile", false)]
    [InlineData("MemberYear", false)]
    public void The_detector_flags_a_product_name_and_spares_an_ordinary_word(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = NamesAProduct(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the scan ─────────────────────────────────────────────────────────────

    private static CoreScan Scan()
    {
        var names = new List<(string Where, string Name)>();
        var types = new HashSet<Type>();
        var memberCount = 0;

        // GetTypes: non-public and nested types too, compiler-generated ones included.
        foreach (var type in Core.GetTypes())
        {
            types.Add(type);
            var where = type.FullName ?? type.Name;
            names.Add((where, type.Namespace ?? ""));
            AddTypeNames(names, where, type);
            if (type.BaseType is { } baseType)
                AddTypeNames(names, where, baseType);
            foreach (var implemented in type.GetInterfaces())
                AddTypeNames(names, where, implemented);
            if (type.IsEnum)
                names.AddRange(Enum.GetNames(type).Select(member => (where, member)));

            foreach (var member in type.GetMembers(Declared))
            {
                memberCount++;
                var memberWhere = $"{where}.{member.Name}";
                names.Add((memberWhere, member.Name));
                switch (member)
                {
                    case FieldInfo field:
                        AddTypeNames(names, memberWhere, field.FieldType);
                        break;
                    case PropertyInfo property:
                        AddTypeNames(names, memberWhere, property.PropertyType);
                        break;
                    case MethodBase method:
                        if (method is MethodInfo { ReturnType: var returnType })
                            AddTypeNames(names, memberWhere, returnType);
                        if (method.IsGenericMethodDefinition)
                        {
                            foreach (var argument in method.GetGenericArguments())
                                AddTypeNames(names, memberWhere, argument);
                        }

                        foreach (var parameter in method.GetParameters())
                        {
                            names.Add((memberWhere, parameter.Name ?? ""));
                            AddTypeNames(names, memberWhere, parameter.ParameterType);
                        }

                        break;
                    case EventInfo { EventHandlerType: { } handlerType }:
                        AddTypeNames(names, memberWhere, handlerType);
                        break;
                }
            }
        }

        return new CoreScan(names, types, memberCount);
    }

    /// <summary>A type's name and, recursively, its generic arguments' and element type's names, so
    /// <c>List&lt;SomeProductThing&gt;</c> is seen too.</summary>
    private static void AddTypeNames(List<(string Where, string Name)> names, string where, Type type)
    {
        names.Add((where, type.Name));
        if (type.HasElementType && type.GetElementType() is { } elementType)
            AddTypeNames(names, where, elementType);
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                AddTypeNames(names, where, argument);
        }
    }

    private static bool NamesAProduct(string name)
    {
        var lowered = name.ToLowerInvariant();
        if (ProductNames.Any(product => lowered.Contains(product, StringComparison.Ordinal)))
            return true;

        var words = Words().Matches(name).Select(match => match.Value.ToLowerInvariant()).ToList();
        for (var start = 0; start < words.Count; start++)
        {
            var run = "";
            for (var end = start; end < words.Count; end++)
            {
                run += words[end];
                if (ProductWords.Contains(run, StringComparer.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The words of a PascalCase, camelCase or snake_case name: an acronym run
    /// (<c>API</c> in <c>APIKey</c>), a capitalised or lower-case word, or a number.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();

    private sealed record CoreScan(IReadOnlyList<(string Where, string Name)> Names, HashSet<Type> Types, int MemberCount);
}
