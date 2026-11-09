using System.Text.Json;
using System.Text.Json.Serialization;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Model;

/// <summary>One named paragraph style (Section 6.5).</summary>
public sealed record ParagraphStyle
{
    public string Name { get; init; } = "Normal";

    /// <summary>null means inherit, which for every style here means Normal's font.</summary>
    public string? Family { get; init; }

    public double SizePt { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public string? Color { get; init; }
    public string? Shading { get; init; }
    public double IndentLeftPt { get; init; }
    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; }
    public double LineSpacing { get; init; } = 1.0;

    /// <summary>1 to 6 for Heading 1 to 6. Used for DOCX outline levels and Markdown.</summary>
    public int? OutlineLevel { get; init; }
}

/// <summary>
/// The paragraph style sheet (Section 6.5). Shipped inside the .exe as
/// <c>default-styles.json</c> and copied to <c>data/styles.json</c> on first
/// run, so a user can change the styles without a new build.
/// </summary>
/// <remarks>
/// Applying a style sets the paragraph properties and the base run formatting
/// for that paragraph. Direct formatting the user added on top of it stays, the
/// way it does in Word; Clear Formatting is what removes that and returns the
/// paragraph to its style.
/// </remarks>
public sealed class StyleSheet
{
    /// <summary>Every style name, in the order the style picker shows them.</summary>
    public static readonly string[] StyleNames =
    [
        "Normal", "Title", "Subtitle",
        "Heading 1", "Heading 2", "Heading 3", "Heading 4", "Heading 5", "Heading 6",
        "Quote", "Code",
    ];

    public const string NormalStyleName = "Normal";

    private readonly Dictionary<string, ParagraphStyle> _styles;

    private StyleSheet(IEnumerable<ParagraphStyle> styles)
    {
        _styles = new Dictionary<string, ParagraphStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in styles)
        {
            _styles[style.Name] = style;
        }

        // Normal is the fallback for every unknown style name, so it has to
        // exist even if someone edited it out of styles.json.
        if (!_styles.ContainsKey(NormalStyleName))
        {
            _styles[NormalStyleName] = new ParagraphStyle { Name = NormalStyleName };
        }
    }

    /// <summary>The style sheet built into the .exe.</summary>
    public static StyleSheet Default { get; } = LoadEmbedded();

    /// <summary>
    /// The styles in <see cref="StyleNames"/> order, then anything the user
    /// added to styles.json.
    /// </summary>
    public IReadOnlyList<ParagraphStyle> All =>
    [
        ..StyleNames.Where(_styles.ContainsKey).Select(name => _styles[name]),
        .._styles.Values.Where(s => !StyleNames.Contains(s.Name, StringComparer.OrdinalIgnoreCase)),
    ];

    /// <summary>
    /// A style by name, falling back to Normal. An unknown name is not an
    /// error: a DOCX can carry any style name at all (Section 7.2), and those
    /// become Normal with their formatting applied directly.
    /// </summary>
    public ParagraphStyle this[string? name] =>
        name is not null && _styles.TryGetValue(name, out var style)
            ? style
            : _styles[NormalStyleName];

    public bool Contains(string name) => _styles.ContainsKey(name);

    /// <summary>
    /// Loads <c>data/styles.json</c>, or falls back to the built-in sheet when
    /// the file is missing or damaged. A damaged file is never overwritten
    /// silently: the user may have been editing it.
    /// </summary>
    public static StyleSheet LoadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Default;
            }

            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize(stream, StyleSheetJson.Default.StyleSheetFile);
            if (dto?.Styles is not { Count: > 0 })
            {
                Log.Warn("styles.json held no styles; using the built-in sheet");
                return Default;
            }

            return new StyleSheet(dto.Styles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"styles.json could not be read; using the built-in sheet ({ex.GetType().Name})");
            return Default;
        }
    }

    /// <summary>
    /// Writes the built-in sheet to <paramref name="path"/> if nothing is there
    /// yet, so the user has something to edit. Never overwrites.
    /// </summary>
    public static void WriteDefaultIfMissing(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return;
            }

            using var source = EmbeddedStream();
            using var target = File.Create(path);
            source.CopyTo(target);
            Log.Info("wrote the default styles.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"styles.json could not be written ({ex.GetType().Name})");
        }
    }

    private static StyleSheet LoadEmbedded()
    {
        try
        {
            using var stream = EmbeddedStream();
            var dto = JsonSerializer.Deserialize(stream, StyleSheetJson.Default.StyleSheetFile);
            if (dto?.Styles is { Count: > 0 })
            {
                return new StyleSheet(dto.Styles);
            }
        }
        catch (JsonException ex)
        {
            // A broken embedded resource is a build problem, not a user problem,
            // so it must not take the app down at startup.
            Log.Error("the built-in style sheet could not be parsed", ex);
        }

        return new StyleSheet([new ParagraphStyle { Name = NormalStyleName }]);
    }

    private static Stream EmbeddedStream() =>
        typeof(StyleSheet).Assembly.GetManifestResourceStream("ScratchpadLite.Core.default-styles.json")
        ?? throw new InvalidOperationException(
            "default-styles.json is missing from the assembly. Check the EmbeddedResource in "
            + "ScratchpadLite.Core.csproj; its LogicalName must be ScratchpadLite.Core.default-styles.json.");
}

/// <summary>The shape of default-styles.json and data/styles.json.</summary>
public sealed class StyleSheetFile
{
    public List<ParagraphStyle> Styles { get; init; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(StyleSheetFile))]
public partial class StyleSheetJson : JsonSerializerContext;
