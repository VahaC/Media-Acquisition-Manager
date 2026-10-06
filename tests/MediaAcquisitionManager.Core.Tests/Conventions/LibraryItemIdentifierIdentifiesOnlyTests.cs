using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Core.Tests.Conventions;

/// <summary>
/// MAM-34 (0.2.1) — the app identifies a library item as the owner's link says and nothing else (Vasyl, 2026-10-06, ADR 0026):
/// the library's own Identify — a search by the id, then Apply, which replaces the item's title, overview, images and ids —,
/// exactly what pressing "Identify" in the library does. The contract makes that hold for every library by having no way to do
/// otherwise: no member of <see cref="IMediaLibraryItemIdentifier"/> — nor of an interface it extends, nor of the library types
/// its calls take and give (the request, the identification, its outcome and refusal), nor any parameter of theirs — may be
/// named for a refresh, a rescan, an image, a title, a lock, a delete, a replace, a move, a merge, an edit, an update or
/// metadata, and no name may be made for identifying other than the one <see cref="IMediaLibraryItemIdentifier.IdentifyAsync"/>.
/// A member that needs such a name is a decision for an ADR, not a quiet addition.
/// </summary>
/// <remarks>
/// The scan reads names only, never type names: <c>LibraryIdentifyRequest</c> and its siblings are named for what they are.
/// It reads the member and parameter names and every enum member, which is why the refusal for a locked item is
/// <see cref="LibraryIdentifyRefusal.ItemProtected"/> and the word the owner reads lives only in its sentence. There is no
/// <c>replaceAllImages</c> parameter anywhere in the contract: the adapter always sends the <c>true</c> Jellyfin's own button
/// sends, and nothing in the contract could make Identify replace more or less than that button does. The earlier rule, that
/// the app wrote ids only (2026-10-05), had its own convention test; this replaces it.
/// </remarks>
public sealed partial class LibraryItemIdentifierIdentifiesOnlyTests
{
    /// <summary>The words of everything an item identifier must never grow a way to do, matched as whole words of a name.</summary>
    private static readonly string[] ForbiddenWords =
    [
        "refresh", "rescan", "scan", "identify", "metadata",
        "image", "images", "poster", "backdrop", "artwork", "thumb",
        "title", "titles", "rename", "overview", "genre", "genres",
        "lock", "locks", "locked", "unlock",
        "delete", "remove", "erase", "purge",
        "replace", "move", "merge", "edit", "update",
    ];

    /// <summary>The one name for identifying the contract has (see the remarks).</summary>
    private static readonly string AllowedVerb = nameof(IMediaLibraryItemIdentifier.IdentifyAsync);

    [Fact]
    public void No_member_is_named_for_refreshing_images_titles_locks_deleting_replacing_or_editing()
    {
        //Arrange
        var members = MemberNames();

        //Act
        var offenders = members.Where(NamesMoreThanIdentify).Where(name => !string.Equals(name, AllowedVerb, StringComparison.Ordinal)).ToList();

        //Assert
        Assert.True(offenders.Count == 0, $"Item identifier members beyond identifying: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_only_member_that_changes_anything_is_identify_and_it_takes_the_connection_the_request_and_a_token()
    {
        //Arrange
        var identify = typeof(IMediaLibraryItemIdentifier).GetMethod(nameof(IMediaLibraryItemIdentifier.IdentifyAsync))!;

        //Act
        var flagged = MemberNames().Where(NamesMoreThanIdentify).Distinct(StringComparer.Ordinal).ToList();
        var parameters = identify.GetParameters().Select(parameter => parameter.Name).ToList();

        //Assert
        // The detector does flag the one verb — so a second name for identifying would fail — and the allowance covers that
        // one member alone.
        Assert.Equal([AllowedVerb], flagged);
        // The connection, the request and a token: no switch to replace more or less than the library's button does.
        Assert.Equal(["settings", "request", "cancellationToken"], parameters);
    }

    [Fact]
    public void The_request_names_the_item_its_kind_the_one_id_and_whether_to_protect_held_ids_and_nothing_else()
    {
        //Arrange
        var constructor = Assert.Single(typeof(LibraryIdentifyRequest).GetConstructors());

        //Act
        var parameters = constructor.GetParameters().Select(parameter => parameter.Name).ToList();
        var properties = typeof(LibraryIdentifyRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToList();

        //Assert
        // No flag that could make Identify replace more or less than Jellyfin's button: there is no replaceAllImages
        // parameter, and the adapter always sends the true the button sends.
        Assert.Equal(
            [
                nameof(LibraryIdentifyRequest.SourceItemId), nameof(LibraryIdentifyRequest.Kind),
                nameof(LibraryIdentifyRequest.Identifier), nameof(LibraryIdentifyRequest.ProtectHeldIds),
            ],
            parameters);
        Assert.Equal(parameters.Order(StringComparer.Ordinal), properties.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(parameters.Concat(properties), name => name!.Contains("Images", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_scan_covers_the_capability_and_every_library_type_it_takes_and_gives()
    {
        //Arrange
        // Guard the guard: the scan sees today's members of the contract, of its request and of its identification, every
        // member of the outcome and the refusal, and the parameters of the call, so a later rename or an added member is
        // scanned too.
        string[] known =
        [
            nameof(IMediaLibraryItemIdentifier.IdentifiableKinds), nameof(IMediaLibraryItemIdentifier.UnidentifiableKindReason),
            nameof(IMediaLibraryItemIdentifier.IdentifyAsync),
            nameof(LibraryIdentifyRequest.SourceItemId), nameof(LibraryIdentifyRequest.Kind),
            nameof(LibraryIdentifyRequest.Identifier), nameof(LibraryIdentifyRequest.ProtectHeldIds),
            nameof(LibraryIdentification.Outcome), nameof(LibraryIdentification.Refusal), nameof(LibraryIdentification.Sent),
            nameof(LibraryIdentification.IdsBefore), nameof(LibraryIdentification.IdsAfter), nameof(LibraryIdentification.Sentence),
            .. Enum.GetNames<LibraryIdentifyOutcome>(),
            .. Enum.GetNames<LibraryIdentifyRefusal>(),
            "settings", "request", "cancellationToken", "kind",
        ];

        //Act
        var members = MemberNames();

        //Assert
        Assert.Subset(members.ToHashSet(StringComparer.Ordinal), known.ToHashSet(StringComparer.Ordinal));
        Assert.True(members.Count >= 25, $"Only {members.Count} names were scanned.");
    }

    [Theory]
    [InlineData("RefreshItemAsync", true)]
    [InlineData("IdentifyAllAsync", true)]
    [InlineData("IdentifyAsync", true)]
    [InlineData("SetPrimaryImageAsync", true)]
    [InlineData("Title", true)]
    [InlineData("LockedFields", true)]
    [InlineData("ItemLocked", true)]
    [InlineData("DeleteItemAsync", true)]
    [InlineData("MoveItemAsync", true)]
    [InlineData("MergeItemsAsync", true)]
    [InlineData("EditItemAsync", true)]
    [InlineData("UpdateItemAsync", true)]
    [InlineData("replaceAllMetadata", true)]
    [InlineData("replaceAllImages", true)]
    [InlineData("MetadataLanguage", true)]
    [InlineData("Identified", false)]
    [InlineData("AlreadyIdentified", false)]
    [InlineData("IdentifiableKinds", false)]
    [InlineData("UnidentifiableKindReason", false)]
    [InlineData("ItemProtected", false)]
    [InlineData("ProtectHeldIds", false)]
    [InlineData("ConflictingId", false)]
    [InlineData("LibraryWritesFiles", false)]
    [InlineData("NotConfirmed", false)]
    [InlineData("SourceItemId", false)]
    [InlineData("IdsBefore", false)]
    public void The_detector_flags_a_name_beyond_identify_and_spares_an_ordinary_one(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = NamesMoreThanIdentify(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the scan ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every member name of the contract and the interfaces it extends, and of every type of its namespace its members take or
    /// give — through generic arguments and property types, so the identification inside a call's result is found —, methods,
    /// properties, events and enum members, but not the accessors the compiler makes for a property; and the name of every
    /// parameter of their methods, so a flag can't hide in one.
    /// </summary>
    private static List<string> MemberNames()
    {
        var contract = typeof(IMediaLibraryItemIdentifier);
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

    private static bool NamesMoreThanIdentify(string name) =>
        Words().Matches(name).Any(match => ForbiddenWords.Contains(match.Value.ToLowerInvariant(), StringComparer.Ordinal));

    /// <summary>The words of a PascalCase or camelCase name, as <see cref="CoreIsVendorNeutralTests"/> splits
    /// them.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
