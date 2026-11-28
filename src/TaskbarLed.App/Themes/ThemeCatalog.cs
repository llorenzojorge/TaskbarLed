using System.IO;
using System.Windows;
using System.Windows.Media;
using TaskbarLed.Core.Settings;
using TaskbarLed.Core.Themes;

namespace TaskbarLed.App.Themes;

/// <summary>A selectable look for the panel and its overlays.</summary>
/// <param name="Id">Name stored in settings. Built-in: the XAML file name (Themes/{Id}.xaml). Custom: <c>custom:</c> + file name.</param>
/// <param name="DisplayName">Name shown in the menu.</param>
/// <param name="Uppercase">Show text in capitals (LED and terminal looks) or in normal case (modern looks).</param>
/// <param name="Custom">The user's JSON theme, or null for a built-in one.</param>
/// <param name="Sound">Voice of the notification sounds while this theme is on (unless the user picked one).</param>
public sealed record ThemeInfo(string Id, string DisplayName, bool Uppercase, CustomTheme? Custom = null, SoundStyle Sound = SoundStyle.Chime);

/// <summary>
/// Built-in themes plus the user's own JSON themes. To add a built-in one: create Themes/YourTheme.xaml with
/// every <c>Led.*</c> key (copy Matrix.xaml), then add a line here. Switching is live: all colors use DynamicResource.
/// </summary>
public static class ThemeCatalog
{
    public const string DefaultId = PanelSettings.DefaultTheme;

    // The dictionary we last put in the application resources, so the next Apply replaces it.
    private static ResourceDictionary? s_applied;

    // Menu order: the default first, then the other terminal looks, then the modern cards.
    public static IReadOnlyList<ThemeInfo> All { get; } =
    [
        new("ClaudeCode", "Claude Code", Uppercase: false),
        new("ModernTerminal", "Modern Terminal", Uppercase: false, Sound: SoundStyle.Bell),
        new("Matrix", "Matrix", Uppercase: true, Sound: SoundStyle.Retro),
        new("Mono", "Mono", Uppercase: true, Sound: SoundStyle.Retro),
        new("Minimal", "Minimal", Uppercase: false),
        new("Glass", "Glass", Uppercase: false, Sound: SoundStyle.Bell),
        new("Paper", "Paper", Uppercase: false),
        new("LiquidGlass", "Liquid Glass", Uppercase: false),
        new("AmberTree", "Amber Tree", Uppercase: false),
        new("CompactCharcoal", "Compact Charcoal", Uppercase: false),
        new("AmberRail", "Amber Rail", Uppercase: false),
        new("ClearList", "Clear List", Uppercase: false),
        new("EditorialIndex", "Editorial Index", Uppercase: false),
        new("CobaltWindow", "Cobalt Window", Uppercase: false),
        new("VioletPhosphor", "Violet Phosphor", Uppercase: false),
        new("LineMap", "Line Map", Uppercase: false),
        new("OliveLcd", "Olive LCD", Uppercase: true),
    ];

    /// <summary>The user's themes from the themes folder, as of the last <see cref="LoadCustom"/>.</summary>
    public static IReadOnlyList<ThemeInfo> Custom { get; private set; } = [];

    /// <summary>Built-in themes followed by the user's own.</summary>
    public static IEnumerable<ThemeInfo> Available => All.Concat(Custom);

    /// <summary>Where users drop their JSON themes: <c>%APPDATA%\TaskbarLed\themes</c>.</summary>
    public static string CustomFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarLed", "themes");

    // Ids that were renamed, so a saved choice keeps working.
    private static readonly Dictionary<string, string> RenamedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Terminal"] = "ModernTerminal",
        ["Clay"] = "ClaudeCode",
    };

    /// <summary>The known theme matching <paramref name="id"/> (or its former name), or the default.</summary>
    public static ThemeInfo Resolve(string? id)
    {
        if (id is not null && RenamedIds.TryGetValue(id, out var renamed))
        {
            id = renamed;
        }

        return Find(id) ?? Find(DefaultId)!;
    }

    /// <summary>
    /// Reads every <c>*.json</c> file of <paramref name="folder"/> as a theme. Never throws: files that are
    /// too big, invalid or duplicated are skipped and returned as messages for the log.
    /// </summary>
    public static IReadOnlyList<string> LoadCustom(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var themes = new List<ThemeInfo>();
        var problems = new List<string>();
        try
        {
            if (!Directory.Exists(folder))
            {
                Custom = [];
                return problems;
            }

            var files = new DirectoryInfo(folder).EnumerateFiles("*.json")
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                if (themes.Count >= CustomTheme.MaxThemes)
                {
                    problems.Add($"Theme '{file.Name}' skipped: more than {CustomTheme.MaxThemes} themes.");
                    continue;
                }

                if (file.Length > CustomTheme.MaxFileBytes)
                {
                    problems.Add($"Theme '{file.Name}' skipped: file is larger than 16 KB.");
                    continue;
                }

                if (!CustomTheme.TryParse(file.Name, File.ReadAllText(file.FullName), out var theme, out var error))
                {
                    problems.Add($"Theme '{file.Name}' skipped: {error}.");
                    continue;
                }

                if (themes.Any(t => string.Equals(t.Id, theme!.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"Theme '{file.Name}' skipped: another file has the same name.");
                    continue;
                }

                themes.Add(new ThemeInfo(theme!.Id, theme.Name, theme.Uppercase, theme));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Themes folder could not be read: {ex.Message}");
        }

        Custom = themes;
        return problems;
    }

    /// <summary>Swaps the theme dictionary in the application resources.</summary>
    public static void Apply(ResourceDictionary resources, string? id)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var info = Resolve(id);
        Swap(resources, info.Custom is { } custom ? Build(custom) : new ResourceDictionary { Source = Uri(info.Id) });
    }

    /// <summary>Applies a user theme that is not in the catalog (the demo renderer previews the example with it).</summary>
    internal static void ApplyCustom(ResourceDictionary resources, CustomTheme custom) => Swap(resources, Build(custom));

    private static void Swap(ResourceDictionary resources, ResourceDictionary theme)
    {
        // The dictionary we applied last, or the one App.xaml starts with.
        var current = s_applied is not null && resources.MergedDictionaries.Contains(s_applied)
            ? s_applied
            : resources.MergedDictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);

        if (current is null)
        {
            resources.MergedDictionaries.Add(theme);
        }
        else
        {
            resources.MergedDictionaries[resources.MergedDictionaries.IndexOf(current)] = theme;
        }

        s_applied = theme;
    }

    /// <summary>Pack URI of a built-in theme dictionary.</summary>
    public static Uri Uri(string id) => new($"pack://application:,,,/TaskbarLed;component/Themes/{id}.xaml", UriKind.Absolute);

    /// <summary>
    /// A user theme becomes the default theme with some values replaced. Only brushes, a color, a number,
    /// a font name and a string are created here, from values <see cref="CustomTheme"/> already validated.
    /// </summary>
    private static ResourceDictionary Build(CustomTheme custom)
    {
        var dictionary = new ResourceDictionary();
        dictionary.MergedDictionaries.Add(new ResourceDictionary { Source = Uri(DefaultId) });

        foreach (var (key, hex) in custom.Colors)
        {
            dictionary[key] = Frozen(new SolidColorBrush(Parse(hex)));
        }

        if (custom.GlowColor is { } glow)
        {
            dictionary["Led.GlowColor"] = Parse(glow);
        }

        if (custom.GlowOpacity is { } opacity)
        {
            dictionary["Led.GlowOpacity"] = opacity;
        }

        if (custom.FontFamily is { } font)
        {
            dictionary["Led.FontFamily"] = new FontFamily(font);
        }

        if (custom.FeedPrefix is { } prefix)
        {
            dictionary["Led.FeedPrefix"] = prefix;
        }

        return dictionary;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static ThemeInfo? Find(string? id) =>
        Available.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
