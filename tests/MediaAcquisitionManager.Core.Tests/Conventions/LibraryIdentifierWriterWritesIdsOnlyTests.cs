using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Core.Tests.Conventions;

/// <summary>
/// MAM-34 — the app writes the owner's links into a library as ids only (Vasyl, 2026-10-05): no identify, no images,
/// titles or locks, and it never deletes an item. The contract makes that hold for every library by having no way to do
/// otherwise: no member of <see cref="IMediaLibraryIdentifierWriter"/> — nor of an interface it extends, nor of the
/// library types its calls take and give (the change, its edits and rules, the write, the lookup), nor any parameter of
/// theirs — may be named for any of them. A member that needs such a name is a decision for an ADR, not a quiet
/// addition.
/// </summary>
/// <remarks>
/// One such decision stands (Vasyl, 2026-10-06, ADR 0026): <see cref="IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync"/>,
/// the library's own lookup of an item's <em>missing</em> metadata after its ids are written — Jellyfin's "Search for
/// missing metadata", which replaces nothing it holds. Written alone, the ids changed nothing anyone could see in
/// Jellyfin, which fetches an existing item's metadata only when it refreshes it. It is the one name allowed, and it
/// takes no flag that could make the lookup replace anything: an identify, an image, a delete or any other refresh
/// member — "replace all metadata" above all — still fails here. What a library does on its own when it saves an item is
/// the library's, not a member of this contract: Jellyfin's save of a series copies the series' name and age ratings onto
/// its seasons and episodes, which Vasyl accepted when he had series written like films (2026-10-06, ADR 0026).
/// </remarks>
public sealed partial class LibraryIdentifierWriterWritesIdsOnlyTests
{
    /// <summary>The words of everything but an item's ids, matched as whole words of a name.</summary>
    private static readonly string[] ForbiddenWords =
    [
        "refresh", "rescan", "scan", "identify", "metadata",
        "image", "images", "poster", "backdrop", "artwork", "thumb",
        "title", "titles", "rename", "overview", "genre", "genres",
        "lock", "locks", "locked", "unlock",
        "delete", "remove", "erase", "purge",
        "replace",
    ];

    /// <summary>The names ADR 0026 allows beyond ids: the one lookup of an item's missing metadata (see the remarks).</summary>
    private static readonly string[] AllowedBeyondIds = [nameof(IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync)];

    [Fact]
    public void No_identifier_writer_member_is_named_for_refreshing_identifying_images_titles_locks_or_deleting()
    {
        //Arrange
        var members = MemberNames();

        //Act
        var offenders = members.Where(NamesMoreThanIds).Except(AllowedBeyondIds, StringComparer.Ordinal).ToList();

        //Assert
        Assert.True(offenders.Count == 0, $"Identifier writer members beyond ids: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_one_name_beyond_ids_is_the_missing_metadata_lookup_and_it_takes_nothing_that_could_replace()
    {
        //Arrange
        var lookup = typeof(IMediaLibraryIdentifierWriter).GetMethod(nameof(IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync))!;

        //Act
        var flagged = MemberNames().Where(NamesMoreThanIds).Distinct(StringComparer.Ordinal).ToList();
        var parameters = lookup.GetParameters().Select(parameter => parameter.Name).ToList();

        //Assert
        // The allowance is needed — the detector does flag it — and covers that one member alone.
        Assert.Equal(AllowedBeyondIds, flagged);
        // The item, its kind and the connection: no switch to replace what the library holds, nor to name what to fetch.
        Assert.Equal(["settings", "sourceItemId", "kind", "cancellationToken"], parameters);
    }

    [Fact]
    public void The_scan_covers_the_writer_and_every_library_type_it_takes_and_gives()
    {
        //Arrange
        // Guard the guard: the scan sees today's members of the contract and of its change, edits, rules, write and
        // lookup, and the parameters of the calls, so a new one is scanned too.
        string[] known =
        [
            nameof(IMediaLibraryIdentifierWriter.WritableKinds), nameof(IMediaLibraryIdentifierWriter.UnwritableKindReason),
            nameof(IMediaLibraryIdentifierWriter.WriteIdentifiersAsync), nameof(IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync),
            nameof(LibraryIdentifierChange.SourceItemId), nameof(LibraryIdentifierChange.Kind), nameof(LibraryIdentifierChange.Edits),
            nameof(LibraryIdentifierEdit.Scheme), nameof(LibraryIdentifierEdit.Value), nameof(LibraryIdentifierEdit.Rule), nameof(LibraryIdentifierEdit.Expected),
            nameof(LibraryIdentifierEditRule.Always), nameof(LibraryIdentifierEditRule.OnlyWhereAbsent), nameof(LibraryIdentifierEditRule.OnlyWhereExpected),
            nameof(LibraryIdentifierWrite.Before), nameof(LibraryIdentifierWrite.After), nameof(LibraryIdentifierWrite.Applied),
            nameof(LibraryIdentifierWrite.Sent), nameof(LibraryIdentifierWrite.Confirmed), nameof(LibraryIdentifierWrite.Problem),
            nameof(LibraryMetadataLookup.SourceItemId),
            "settings", "change", "sourceItemId", "kind", "cancellationToken",
        ];

        //Act
        var members = MemberNames();

        //Assert
        Assert.Subset(members.ToHashSet(StringComparer.Ordinal), known.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("RefreshItemAsync", true)]
    [InlineData("IdentifyAsync", true)]
    [InlineData("SetPrimaryImageAsync", true)]
    [InlineData("Title", true)]
    [InlineData("LockedFields", true)]
    [InlineData("DeleteItemAsync", true)]
    [InlineData("replaceAllMetadata", true)]
    [InlineData("replaceAllImages", true)]
    [InlineData("RequestMetadataLookupAsync", true)]
    [InlineData("WriteIdentifiersAsync", false)]
    [InlineData("OnlyWhereExpected", false)]
    [InlineData("SourceItemId", false)]
    [InlineData("Applied", false)]
    [InlineData("Confirmed", false)]
    public void The_detector_flags_a_name_beyond_ids_and_spares_an_ordinary_one(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = NamesMoreThanIds(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the scan ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every member name of the contract and the interfaces it extends, and of every type of its namespace its members
    /// take or give — through generic arguments and property types, so an edit inside a change's list is found —
    /// methods, properties, events and enum members, but not the accessors the compiler makes for a property; and the
    /// name of every parameter of their methods, so a flag can't hide in one.
    /// </summary>
    private static List<string> MemberNames()
    {
        var contract = typeof(IMediaLibraryIdentifierWriter);
        var scanned = new HashSet<Type>();
        var pending = new Queue<Type>([contract, .. contract.GetInterfaces()]);
        var names = new List<string>();
        while (pending.TryDequeue(out var type))
        {
            if (!scanned.Add(type))
                continue;
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (member is MethodInfo { IsSpecialName: true } || (type.IsEnum && member is FieldInfo { IsSpecialName: true }))
                    continue;
                names.Add(member.Name);
                if (member is MethodBase method)
                    names.AddRange(method.GetParameters().Select(parameter => parameter.Name).OfType<string>());
                foreach (var reached in ReachedTypes(member))
                {
                    if (string.Equals(reached.Namespace, contract.Namespace, StringComparison.Ordinal))
                        pending.Enqueue(reached);
                }
            }
        }

        return names;
    }

    /// <summary>The types a member takes or gives, with every generic argument and array element inside them.</summary>
    private static IEnumerable<Type> ReachedTypes(MemberInfo member)
    {
        IEnumerable<Type> direct = member switch
        {
            MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };
        return direct.SelectMany(Unwrapped);
    }

    private static IEnumerable<Type> Unwrapped(Type type) =>
        type.HasElementType
            ? Unwrapped(type.GetElementType()!)
            : [type, .. type.GetGenericArguments().SelectMany(Unwrapped)];

    private static bool NamesMoreThanIds(string name) =>
        Words().Matches(name).Any(match => ForbiddenWords.Contains(match.Value.ToLowerInvariant(), StringComparer.Ordinal));

    /// <summary>The words of a PascalCase or camelCase name, as <see cref="CoreIsVendorNeutralTests"/> splits
    /// them.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
