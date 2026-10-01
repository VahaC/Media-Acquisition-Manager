using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

/// <summary>
/// The folder a qBittorrent category saves to, worked out the way qBittorrent itself does
/// (<c>categorySavePath</c>) from what <c>torrents/categories</c> lists (MAM-11): that answer carries
/// each category's stored option, never the resolved folder, and the option may be empty (the implicit
/// folder) or relative (under the default save path).
/// </summary>
/// <remarks>
/// <para>
/// Read from qBittorrent's source (<c>src/base/bittorrent/session.cpp</c> of 4.4.5,
/// <c>sessionimpl.cpp</c> of 4.6.7, 5.1.4 and 5.2.3, <c>src/base/utils/fs.cpp</c> of each):
/// </para>
/// <list type="bullet">
/// <item>an absolute save path is the folder, in every version;</item>
/// <item>a relative one sits under the default save path, in every version, a subcategory's too;</item>
/// <item>from 4.5, a save path is qBittorrent's own <c>Path</c>, cleaned by Qt's <c>QDir::cleanPath</c> and
/// listed with <c>/</c> separators (<c>Path::data()</c>), so a Windows host's UNC folder
/// <c>\\nas\media\Movies</c> comes as <c>//nas/media/Movies</c>, while any other host's cleaning folds a
/// leading <c>//</c> into one <c>/</c>; 4.4 lists the save path as it was typed, where a leading <c>//</c>
/// may be either;</item>
/// <item>an empty one is the implicit folder: up to 4.6, the default save path and the whole name
/// (<c>Movies/4K</c> as two folders); from 5.2, the parent category's folder and the last part of the
/// name, a parent qBittorrent doesn't list counting as one with an empty save path; 5.0 and 5.1 do
/// either, as their <c>use_subcategories</c> option says, which only <c>app/preferences</c> reports
/// — and the app doesn't read that (it carries the Web UI's API key and the proxy and mail passwords),
/// so a subcategory's implicit folder there is unknown. A top-level category's implicit folder is the
/// same either way.</item>
/// </list>
/// <para>
/// No guess anywhere: a folder that can't be worked out exactly is <c>null</c>, which keeps automatic
/// replacement off for the destination (<c>DestinationPathCheck</c>). That covers a name qBittorrent
/// would rewrite before using it as a folder name, a save path that isn't one, a leading <c>//</c> that
/// could be a UNC folder or a POSIX one, a save path with a backslash from 4.5 on under a Windows default
/// save path (no Windows host lists one), and a default save path that isn't a full path.
/// </para>
/// </remarks>
internal static class QBittorrentCategoryPaths
{
    /// <summary>Linux's <c>PATH_MAX</c>, what a file path column holds: a longer folder is not one.</summary>
    private const int PathMaximumLength = PhysicalFileEntity.ServicePathMaximumLength;

    /// <summary>How a Windows host's UNC folder starts once qBittorrent has turned its separators into
    /// <c>/</c>.</summary>
    private const string UncPrefix = "//";

    /// <summary>4.5 keeps every folder as its own <c>Path</c>, cleaned by Qt's <c>QDir::cleanPath</c>, which
    /// keeps a leading <c>//</c> only on Windows, where it starts a UNC folder.</summary>
    private static readonly Version CleanedPathsVersion = new(4, 5, 0);

    /// <summary>5.0 lets a subcategory's implicit folder follow its parent's, when <c>use_subcategories</c>
    /// is on (qBittorrent PR #19957).</summary>
    private static readonly Version SubcategoryOptionVersion = new(5, 0, 0);

    /// <summary>5.2 removed <c>use_subcategories</c>: subcategories always follow their parent
    /// (qBittorrent PR #23585, Web API 2.15.0).</summary>
    private static readonly Version SubcategoriesAlwaysVersion = new(5, 2, 0);

    /// <summary>
    /// What qBittorrent's <c>Utils::Fs::toValidPath</c> (and 4.4's <c>toValidFileSystemName</c>) replaces in a
    /// category's name before it becomes a folder name: <c>[:?"*&lt;&gt;|]</c>, the same set from 4.4.1 to
    /// 5.2.3.
    /// </summary>
    private static readonly char[] ReplacedCharacters = [':', '?', '"', '*', '<', '>', '|'];

    /// <summary>
    /// Whether working out where any of <paramref name="categories"/> saves needs qBittorrent's default save
    /// path — so the adapter asks for it only when some category does.
    /// </summary>
    /// <param name="version">The qBittorrent version that listed them.</param>
    /// <param name="categories">The names of the categories to work out.</param>
    /// <param name="savePaths">Every category qBittorrent listed, by name, with its stored save path
    /// (<c>null</c> when its entry gave none as text).</param>
    public static bool NeedsDefaultSavePath(
        Version version, IEnumerable<string> categories, IReadOnlyDictionary<string, string?> savePaths)
    {
        var asked = false;
        foreach (var category in categories)
        {
            Folder(version, category, savePaths, () =>
            {
                asked = true;
                return null;
            });
        }

        return asked;
    }

    /// <summary>
    /// The folder <paramref name="category"/> saves to as qBittorrent sees it, in its canonical form
    /// (<see cref="NormalizedPath.ToString"/>), or <c>null</c> when it can't be worked out exactly.
    /// </summary>
    /// <param name="version">The qBittorrent version that listed it.</param>
    /// <param name="category">The category's name.</param>
    /// <param name="savePaths">Every category qBittorrent listed, by name, with its stored save path
    /// (<c>null</c> when its entry gave none as text).</param>
    /// <param name="defaultSavePath">What <c>app/defaultSavePath</c> answered; <c>null</c> when it wasn't
    /// asked.</param>
    public static string? FolderOf(
        Version version, string category, IReadOnlyDictionary<string, string?> savePaths, string? defaultSavePath)
    {
        // MAM-11 — a default save path with a leading "//" is unknown: 4.4 stores it with a Windows host's
        // backslashes turned into "/" (Utils::Fs::toUniformPath), so it may be a Windows host's UNC folder
        // or a POSIX one, and 4.5 and later never send one (a Windows host's comes with backslashes,
        // Path::toString(); any other host's is cleaned to one "/").
        NormalizedPath? root = defaultSavePath is { Length: <= PathMaximumLength }
            && !defaultSavePath.StartsWith(UncPrefix, StringComparison.Ordinal)
            && NormalizedPath.TryParse(defaultSavePath, out var parsed, out _)
            ? parsed
            : null;
        var folder = Folder(version, category, savePaths, () => root)?.ToString();
        return folder is { Length: <= PathMaximumLength } ? folder : null;
    }

    /// <summary>
    /// <c>categorySavePath</c> of <paramref name="category"/>. <paramref name="defaultSavePath"/> is asked
    /// only for a folder under it, and only once the category's own name has not already made the folder
    /// unknown.
    /// </summary>
    private static NormalizedPath? Folder(
        Version version, string category, IReadOnlyDictionary<string, string?> savePaths, Func<NormalizedPath?> defaultSavePath)
    {
        // 5.2's categoryOptions() of a name it doesn't hold is the default options: an empty save path.
        var savePath = savePaths.TryGetValue(category, out var stored) ? stored : "";
        if (savePath is null)
            return null;

        if (savePath.Length > 0)
        {
            // MAM-11 — NormalizedPath reads "//nas/media" as the POSIX /nas/media, which on a Windows host
            // is the wrong folder in the wrong style.
            if (savePath.StartsWith(UncPrefix, StringComparison.Ordinal))
                return UncFolder(version, savePath);

            // MAM-11 — from 4.5 a Windows host lists its folders with "/" only (Path::data()), so a save path
            // with a backslash comes from another host, where "\\nas\media" or "D:\Torrents" is a relative
            // path whose folder names hold the backslashes — not the Windows folder NormalizedPath would
            // read. It sits under a POSIX default save path; under a Windows one the two answers contradict
            // each other, and the folder is unknown.
            if (version >= CleanedPathsVersion && savePath.Contains('\\', StringComparison.Ordinal) && savePath[0] != '/')
            {
                return defaultSavePath() is { Style: PathStyle.Posix } posixRoot && RelativeSegments(savePath, PathStyle.Posix) is { } names
                    ? posixRoot.Append(names)
                    : null;
            }

            if (NormalizedPath.TryParse(savePath, out var absolute, out var error))
                return absolute;
            // A "." or "..", a control character or only whitespace: not a folder this app takes as
            // written, and qBittorrent's cleaning of it is not repeated here.
            if (error != PathParseError.NotAbsolute)
                return null;

            // Under the default save path in every version, a subcategory's too.
            return defaultSavePath() is { } root && RelativeSegments(savePath, root.Style) is { } relative
                ? root.Append(relative)
                : null;
        }

        if (version < SubcategoryOptionVersion)
        {
            // Up to 4.6 the implicit folder is the whole name, "Movies/4K" as two folders.
            return NameSegments(category) is { } segments && defaultSavePath() is { } root
                ? root.Append(segments)
                : null;
        }

        var separator = category.LastIndexOf('/');
        if (separator < 0)
        {
            // A top-level category: the default save path and its name, whatever use_subcategories says.
            return NameSegments(category) is { } segments && defaultSavePath() is { } root
                ? root.Append(segments)
                : null;
        }

        // MAM-11 — 5.0 and 5.1 put a subcategory under its parent's folder or under the default save path
        // as use_subcategories says, and that option is only in app/preferences, which the app never reads.
        if (version < SubcategoriesAlwaysVersion)
            return null;

        return NameSegments(category[(separator + 1)..]) is { } last
            && Folder(version, category[..separator], savePaths, defaultSavePath) is { } parent
            ? parent.Append(last)
            : null;
    }

    /// <summary>
    /// The folder a save path with a leading <c>//</c> names: from 4.5 a Windows host's UNC folder,
    /// <c>//nas/media/Movies</c> read as <c>\\nas\media\Movies</c>. <c>null</c> up to 4.4, where it may be
    /// a POSIX folder as well, and for a third leading <c>/</c> or a missing share, which name no UNC
    /// folder.
    /// </summary>
    private static NormalizedPath? UncFolder(Version version, string savePath) =>
        version >= CleanedPathsVersion
        && savePath.Length > UncPrefix.Length && savePath[UncPrefix.Length] != '/'
        // No backslash to keep apart: 4.5's cleaning has turned a Windows host's into "/".
        && NormalizedPath.TryParse(savePath.Replace('/', '\\'), out var folder, out _)
            ? folder
            : null;

    /// <summary>
    /// The folder names <paramref name="name"/> becomes, or <c>null</c> when qBittorrent would change it
    /// on the way: a character <see cref="ReplacedCharacters"/> lists, or a folder name it would trim,
    /// clean away or split differently.
    /// </summary>
    /// <remarks>
    /// Whitespace at either end of a folder name counts in every version: 4.4 trims the whole name
    /// (<c>toValidFileSystemName</c>), 5.3 trims each folder name (<c>toValidFileName</c> on qBittorrent's
    /// master branch), and 4.5 to 5.2 keep it. Rather than follow which version does which, such a
    /// name's folder is unknown.
    /// </remarks>
    private static string[]? NameSegments(string name)
    {
        // A backslash is no part of a category name qBittorrent accepts (isValidCategoryName), and on a
        // Windows host it would separate folders.
        if (name.IndexOfAny(ReplacedCharacters) >= 0 || name.Contains('\\', StringComparison.Ordinal))
            return null;

        var segments = name.Split('/');
        // An empty folder name ("A//B", a leading or trailing "/") or a "." or "..": qBittorrent's path
        // cleaning would fold it away, so the folder isn't the name as written.
        return segments.Any(segment => segment.Length == 0 || segment is "." or ".." || !string.Equals(segment, segment.Trim(), StringComparison.Ordinal))
            ? null
            : segments;
    }

    /// <summary>
    /// The folder names of a relative save path, under a default save path of <paramref name="style"/>:
    /// <c>/</c> separates them everywhere, <c>\</c> only on a Windows host (where qBittorrent turns it into
    /// <c>/</c>). <c>null</c> for a <c>.</c> or <c>..</c>, which qBittorrent would resolve.
    /// </summary>
    private static string[]? RelativeSegments(string savePath, PathStyle style)
    {
        char[] separators = style == PathStyle.Windows ? ['/', '\\'] : ['/'];
        var segments = savePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 || segments.Any(segment => segment is "." or "..") ? null : segments;
    }
}
