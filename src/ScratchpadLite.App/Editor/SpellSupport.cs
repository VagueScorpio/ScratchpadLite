using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using ScratchpadLite.Core.Platform;

using WpfBlock = System.Windows.Documents.Block;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// The spell checker built into WPF, which is the Windows one
/// (Section 6.11): red squiggles, suggestions in the context menu, the
/// language setting, and a custom dictionary kept as a lex file in the data
/// folder.
///
/// Startup rule (Section 2.1): it is switched on after the first frame,
/// never before, because turning it on loads the Windows spell checker.
/// </summary>
/// <remarks>
/// Almost nothing here is spell checking. WPF does the checking; what this
/// class does is the four things around it that WPF leaves to the application:
/// which languages the PC actually has, the custom dictionary file, the
/// suggestions menu, and switching the whole thing off on a document large
/// enough that checking it would be felt while typing.
/// </remarks>
internal static class SpellSupport
{
    /// <summary>
    /// Section 6.11 says to mark a Code paragraph as having no linguistic
    /// content, and to find out which tag WPF honours. It honours both, because
    /// it hands the tag to the Windows spell checker, which has no dictionary
    /// for either - but <c>zxx</c> is the registered ISO 639-2 code for "no
    /// linguistic content" and <c>x-none</c> is a private-use tag, so this uses
    /// <c>zxx</c>. Recorded here because the blueprint asks.
    /// </summary>
    internal const string NoLanguageTag = "zxx";

    /// <summary>
    /// The languages WPF falls back on when Windows will not say what it has.
    /// These five are the ones WPF shipped with before it started asking the
    /// platform, so they are the safest possible guess.
    /// </summary>
    private static readonly string[] FallbackLanguages = ["en-US", "en-GB", "de-DE", "es-ES", "fr-FR"];

    private static readonly Lazy<IReadOnlyList<string>> Installed = new(ReadInstalledLanguages);

    /// <summary>
    /// Turns the checker on or off. Called after the first frame, never in the
    /// constructor: turning it on loads the Windows spell checker
    /// (Section 2.1).
    /// </summary>
    /// <param name="maxCharacters">
    /// The size above which to leave it off, or zero for no limit
    /// (<see cref="Settings.SpellCheckMaxCharacters"/>).
    /// </param>
    /// <returns>Whether it ended up on.</returns>
    internal static bool Enable(RichTextBox editor, bool wanted, string language, int maxCharacters = 0)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var on = wanted && !IsTooLarge(editor.Document, maxCharacters);

        editor.SpellCheck.IsEnabled = on;

        if (!on)
        {
            return false;
        }

        SetLanguage(editor, language);
        AttachUserDictionary(editor);

        return true;
    }

    /// <summary>
    /// Whether this document is over the size the user set for leaving spell
    /// check off (Section 6.11). Zero means no limit, which is the default.
    /// </summary>
    /// <remarks>
    /// The limit exists because Section 6.11 asked for it; the default is off
    /// because the measurement said it would not help. Phase 7 timed a
    /// keystroke in a 300-page document at 18.7 ms with spell check on and
    /// 18.9 ms with it off - the cost at that size is WPF's rich text control,
    /// not the Windows spell checker.
    /// </remarks>
    internal static bool IsTooLarge(FlowDocument? document, int maxCharacters) =>
        maxCharacters > 0 && document is not null && Length(document) > maxCharacters;

    /// <summary>
    /// The language the squiggles use. A tag Windows has no dictionary for is
    /// not an error: nothing is flagged, which is the right answer for a
    /// language this PC cannot check.
    /// </summary>
    internal static void SetLanguage(RichTextBox editor, string language)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (Tag(language) is { } tag)
        {
            editor.Language = tag;
        }
    }

    /// <summary>
    /// The spell-check languages installed in Windows (Section 6.11), asked of
    /// Windows itself rather than assumed.
    /// </summary>
    internal static IReadOnlyList<string> Languages() => Installed.Value;

    /// <summary>
    /// Marks every paragraph in the Code style as having no linguistic content,
    /// so a code sample is not covered in red (Section 6.11).
    /// </summary>
    /// <remarks>
    /// WPF has no per-element switch for spell check - <c>SpellCheck.IsEnabled</c>
    /// is a property of the whole box - so the language is what does the work.
    /// </remarks>
    internal static int MarkCodeParagraphs(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Tag(NoLanguageTag) is not { } none)
        {
            return 0;
        }

        var marked = 0;

        foreach (var paragraph in Paragraphs(document.Blocks))
        {
            if (Bridge.SpProps.GetStyleName(paragraph) is "Code")
            {
                paragraph.Language = none;
                marked++;
            }
        }

        return marked;
    }

    // ---------------------------------------------------------- dictionary --

    /// <summary>
    /// Adds a word to <c>data/user-dictionary.lex</c> and makes WPF reload it
    /// (Section 6.11).
    /// </summary>
    /// <remarks>
    /// The reload is the part that is not obvious: WPF reads a custom
    /// dictionary when the URI is added and never looks at the file again, so
    /// appending a word does nothing visible until the URI is removed and added
    /// back.
    /// </remarks>
    internal static void AddToUserDictionary(RichTextBox editor, string word)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        AppendWord(AppPaths.UserDictionaryFile, word.Trim());

        editor.SpellCheck.CustomDictionaries.Clear();
        AttachUserDictionary(editor);
    }

    /// <summary>
    /// Appends a word to a .lex file, creating it with the header Windows wants
    /// (Section 6.11): UTF-16, one word per line, <c>#LID 1033</c> first.
    /// </summary>
    internal static void AppendWord(string path, string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            var words = ReadWords(path);

            if (!words.Add(word))
            {
                return;
            }

            // Rewritten rather than appended to: the file is UTF-16 with a BOM,
            // and appending to it means getting the encoding of the append
            // exactly right every time rather than once.
            Write(path, words);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the user dictionary could not be written ({ex.GetType().Name})");
        }
    }

    /// <summary>The words in a .lex file, without its header.</summary>
    internal static SortedSet<string> ReadWords(string path)
    {
        var words = new SortedSet<string>(StringComparer.CurrentCulture);

        if (!File.Exists(path))
        {
            return words;
        }

        try
        {
            foreach (var line in File.ReadAllLines(path, Encoding.Unicode))
            {
                var word = line.Trim();

                if (word.Length > 0 && !word.StartsWith('#'))
                {
                    words.Add(word);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the user dictionary could not be read ({ex.GetType().Name})");
        }

        return words;
    }

    /// <summary>Writes a .lex file: the header line, then the words.</summary>
    internal static void Write(string path, IEnumerable<string> words)
    {
        // 1033 is en-US. Windows reads the header to decide which language the
        // dictionary belongs to, and a file without it is ignored in silence.
        var lines = new List<string> { "#LID 1033" };
        lines.AddRange(words);

        FileOpsWrite(path, lines);
    }

    private static void FileOpsWrite(string path, IReadOnlyList<string> lines) =>
        Services.FileOps.AtomicWrite(
            path,
            stream =>
            {
                // leaveOpen, because the stream belongs to AtomicWrite: it has
                // to flush it to disk and replace the file after this returns,
                // and a writer that closed it would leave it doing both to a
                // disposed stream.
                using var writer = new StreamWriter(
                    stream, new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 1024, leaveOpen: true);

                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
            },
            keepBak: false);

    private static void AttachUserDictionary(RichTextBox editor)
    {
        var path = AppPaths.UserDictionaryFile;

        try
        {
            if (!File.Exists(path))
            {
                Write(path, []);
            }

            var uri = new Uri(path);

            if (!editor.SpellCheck.CustomDictionaries.Contains(uri))
            {
                editor.SpellCheck.CustomDictionaries.Add(uri);
            }
        }
        catch (Exception ex)
        {
            // A dictionary that will not load must not stop spell check: the
            // squiggles are worth more than the custom words.
            Log.Warn($"the user dictionary could not be attached: {ex.Message}");
        }
    }

    // -------------------------------------------------------- the menu part --

    /// <summary>
    /// What the context menu should offer for the word under
    /// <paramref name="at"/>: up to six suggestions, or nothing at all when the
    /// word is spelled correctly (Section 6.11).
    /// </summary>
    /// <remarks>
    /// The range is carried alongside the error because
    /// <see cref="SpellingError"/> does not know where it is: it offers
    /// <c>Correct</c>, <c>IgnoreAll</c> and the suggestions, and nothing that
    /// says which word they are about. "Add to Dictionary" needs the word
    /// itself, so the range comes from
    /// <c>RichTextBox.GetSpellingErrorRange</c> at the same time as the error.
    /// </remarks>
    internal sealed record Flagged(SpellingError Error, TextRange Range, IReadOnlyList<string> Suggestions)
    {
        internal string Word => Range.Text;
    }

    internal static Flagged? ErrorAt(RichTextBox editor, TextPointer? at)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (at is null || !editor.SpellCheck.IsEnabled)
        {
            return null;
        }

        var error = editor.GetSpellingError(at);

        if (error is null || editor.GetSpellingErrorRange(at) is not { } range)
        {
            return null;
        }

        return new Flagged(error, range, [..error.Suggestions.Take(6)]);
    }

    // ----------------------------------------------------------- the plumbing --

    private static XmlLanguage? Tag(string language)
    {
        try
        {
            return XmlLanguage.GetLanguage(language);
        }
        catch (ArgumentException)
        {
            // A tag the user typed into settings.json by hand. Leaving the
            // language alone is better than failing to switch spell check on.
            Log.Warn($"'{language}' is not a language tag WPF understands");
            return null;
        }
    }

    private static int Length(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text.Length;

    private static IEnumerable<WpfParagraph> Paragraphs(IEnumerable<WpfBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case WpfParagraph paragraph:
                    yield return paragraph;
                    break;

                case List list:
                    foreach (var item in list.ListItems)
                    {
                        foreach (var inner in Paragraphs(item.Blocks))
                        {
                            yield return inner;
                        }
                    }

                    break;

                case Table table:
                    foreach (var group in table.RowGroups)
                    {
                        foreach (var row in group.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                foreach (var inner in Paragraphs(cell.Blocks))
                                {
                                    yield return inner;
                                }
                            }
                        }
                    }

                    break;

                case Section section:
                    foreach (var inner in Paragraphs(section.Blocks))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Asks Windows which languages its spell checker has
    /// (Section 6.11).
    /// </summary>
    /// <remarks>
    /// There is no managed API for this. WPF sets the language on the control
    /// and the Windows spell checker either has a dictionary for it or quietly
    /// has none, so a list built by guessing would offer languages that do
    /// nothing. <c>ISpellCheckerFactory</c> is the component WPF itself talks
    /// to, so asking it directly is asking the same thing WPF will ask.
    /// <para>
    /// Everything here is wrapped: on a Windows install where the component is
    /// missing or refuses, the five languages WPF used to ship with are offered
    /// instead, which is no worse than not having the menu.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ReadInstalledLanguages()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));

            if (type is null || Activator.CreateInstance(type) is not ISpellCheckerFactory factory)
            {
                return FallbackLanguages;
            }

            var tags = new List<string>();

            try
            {
                var enumerator = factory.SupportedLanguages;
                var one = new string[1];

                while (enumerator.Next(1, one, IntPtr.Zero) == 0 && one[0] is { Length: > 0 } tag)
                {
                    tags.Add(tag);
                    one[0] = string.Empty;
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(factory);
            }

            // Windows lists plain "en" alongside "en-US". A bare language with
            // no region is not something to offer in a menu of dictionaries,
            // because WPF wants the tag it can set on the control.
            var usable = tags
                .Where(t => t.Contains('-', StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return usable.Count > 0 ? usable : FallbackLanguages;
        }
        catch (Exception ex)
        {
            Log.Warn($"Windows would not list its spell-check languages: {ex.Message}");
            return FallbackLanguages;
        }
    }

    /// <summary>
    /// The one method of <c>ISpellCheckerFactory</c> this app needs. Declared
    /// in vtable order up to that point, because COM has no other way to find
    /// a method.
    /// </summary>
    [ComImport]
    [Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            get;
        }
    }
}
