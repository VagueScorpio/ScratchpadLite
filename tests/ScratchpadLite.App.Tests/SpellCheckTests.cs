using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using ScratchpadLite.App.Editor;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Spell check (Section 6.11). WPF does the checking, so what is tested here is
/// the four things it leaves to the application: the custom dictionary file,
/// which languages this PC actually has, marking code as not prose, and the
/// size at which checking stops being free.
/// </summary>
[Collection(nameof(AppHost))]
public class SpellCheckTests
{
    // ------------------------------------------------- the user dictionary --

    [Fact]
    public void ANewDictionaryGetsTheHeaderWindowsWants()
    {
        var text = InTempFile(path =>
        {
            SpellSupport.Write(path, ["aardvark"]);

            return File.ReadAllText(path, Encoding.Unicode);
        });

        // Section 6.11: UTF-16, one word per line, "#LID 1033" first. Without
        // the header Windows ignores the file in silence.
        Assert.StartsWith("#LID 1033", text, StringComparison.Ordinal);
        Assert.Contains("aardvark", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDictionaryIsUtf16()
    {
        var bytes = InTempFile(path =>
        {
            SpellSupport.Write(path, ["word"]);

            return File.ReadAllBytes(path);
        });

        // A byte-order mark, and then a character per two bytes. Written as a
        // byte check because "it opened in Notepad" is not the same as "Windows
        // can read it".
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
    }

    [Fact]
    public void ReadWordsSkipsTheHeader()
    {
        var words = InTempFile(path =>
        {
            SpellSupport.Write(path, ["alpha", "beta"]);

            return SpellSupport.ReadWords(path).ToList();
        });

        Assert.Equal(["alpha", "beta"], words);
    }

    [Fact]
    public void AddingAWordKeepsTheOnesAlreadyThere()
    {
        var words = InTempFile(path =>
        {
            SpellSupport.AppendWord(path, "scratchpad");
            SpellSupport.AppendWord(path, "aardvark");

            return SpellSupport.ReadWords(path).ToList();
        });

        Assert.Equal(["aardvark", "scratchpad"], words);
    }

    [Fact]
    public void AddingTheSameWordTwiceAddsItOnce()
    {
        var words = InTempFile(path =>
        {
            SpellSupport.AppendWord(path, "scratchpad");
            SpellSupport.AppendWord(path, "scratchpad");

            return SpellSupport.ReadWords(path).ToList();
        });

        Assert.Equal(["scratchpad"], words);
    }

    [Fact]
    public void ReadingADictionaryThatIsNotThereIsNotAnError()
    {
        var words = SpellSupport.ReadWords(
            Path.Combine(Path.GetTempPath(), $"not-there-{Guid.NewGuid():N}.lex"));

        Assert.Empty(words);
    }

    // ------------------------------------------------------- the languages --

    [Fact]
    public void WindowsIsAskedWhichLanguagesItCanCheck()
    {
        var languages = SpellSupport.Languages();

        // There is no managed API for this, so it is a COM call to the same
        // component WPF itself uses - which means it can fail, and the fallback
        // has to be a usable list rather than nothing.
        Assert.NotEmpty(languages);
    }

    [Fact]
    public void EveryLanguageOfferedHasARegion()
    {
        var languages = SpellSupport.Languages();

        // WPF wants a tag it can put on the control, and a bare "en" with no
        // region is not one. Windows lists both.
        Assert.All(languages, tag => Assert.Contains('-', tag));
    }

    [Fact]
    public void EveryLanguageOfferedIsOneWpfAccepts()
    {
        var accepted = AppHost.Run(() =>
            SpellSupport.Languages()
                .Where(tag =>
                {
                    try
                    {
                        System.Windows.Markup.XmlLanguage.GetLanguage(tag);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                })
                .ToList());

        // A language in the menu that WPF then refuses would be a menu item
        // that throws.
        Assert.Empty(accepted);
    }

    [Fact]
    public void SettingAnImpossibleLanguageDoesNotThrow()
    {
        var language = AppHost.Run(() =>
        {
            var editor = new RichTextBox();

            // What a hand-edited settings.json can contain.
            SpellSupport.SetLanguage(editor, "not a language");

            return editor.Language.IetfLanguageTag;
        });

        // Left as it was, rather than failing to switch spell check on at all.
        Assert.NotNull(language);
    }

    // ------------------------------------------------------- code is not prose --

    [Fact]
    public void CodeParagraphsAreMarkedAsHavingNoLanguage()
    {
        var (marked, codeTag, proseTag) = AppHost.Run(() =>
        {
            var code = new Paragraph(new Run("for (var i = 0; i < n; i++) { frobnicate(); }"));
            var prose = new Paragraph(new Run("This is an ordinary sentence."));

            ScratchpadLite.App.Bridge.SpProps.SetStyleName(code, "Code");
            ScratchpadLite.App.Bridge.SpProps.SetStyleName(prose, "Normal");

            var document = new FlowDocument();
            document.Blocks.Add(code);
            document.Blocks.Add(prose);

            var count = SpellSupport.MarkCodeParagraphs(document);

            return (count, code.Language.IetfLanguageTag, prose.Language.IetfLanguageTag);
        });

        Assert.Equal(1, marked);

        // Section 6.11 asks which tag WPF honours. Both reach the Windows
        // spell checker, which has a dictionary for neither; zxx is the
        // registered code for "no linguistic content", so that is the one used.
        Assert.Equal(SpellSupport.NoLanguageTag, codeTag);
        Assert.NotEqual(SpellSupport.NoLanguageTag, proseTag);
    }

    [Fact]
    public void CodeInsideAListOrATableIsMarkedToo()
    {
        var marked = AppHost.Run(() =>
        {
            var inItem = new Paragraph(new Run("git push"));
            var inCell = new Paragraph(new Run("npm install"));

            ScratchpadLite.App.Bridge.SpProps.SetStyleName(inItem, "Code");
            ScratchpadLite.App.Bridge.SpProps.SetStyleName(inCell, "Code");

            var list = new List();
            list.ListItems.Add(new ListItem(inItem));

            var row = new TableRow();
            row.Cells.Add(new TableCell(inCell));

            var group = new TableRowGroup();
            group.Rows.Add(row);

            var table = new Table();
            table.RowGroups.Add(group);

            var document = new FlowDocument();
            document.Blocks.Add(list);
            document.Blocks.Add(table);

            return SpellSupport.MarkCodeParagraphs(document);
        });

        // A code sample in a bulleted list is still a code sample.
        Assert.Equal(2, marked);
    }

    // ------------------------------------------------------------ the limit --

    /// <summary>
    /// The default. Section 6.11 asked for a size limit in case checking a
    /// 300-page document made typing lag; Phase 7 measured a keystroke at
    /// 18.7 ms with spell check on and 18.9 ms with it off on exactly that
    /// document, so the limit ships switched off and spell check works at
    /// every size.
    /// </summary>
    [Fact]
    public void WithNoLimitSetNoDocumentIsTooLarge()
    {
        var tooLarge = AppHost.Run(() =>
            SpellSupport.IsTooLarge(Document(new string('a', 2_000_000)), maxCharacters: 0));

        Assert.False(tooLarge);
    }

    [Fact]
    public void ADocumentUnderTheLimitIsNotTooLarge()
    {
        var tooLarge = AppHost.Run(() =>
            SpellSupport.IsTooLarge(Document(new string('a', 50_000)), maxCharacters: 700_000));

        Assert.False(tooLarge);
    }

    [Fact]
    public void ADocumentOverTheLimitIsTooLarge()
    {
        var tooLarge = AppHost.Run(() =>
            SpellSupport.IsTooLarge(Document(new string('a', 700_001)), maxCharacters: 700_000));

        Assert.True(tooLarge);
    }

    [Fact]
    public void SpellCheckStaysOffOnADocumentOverTheLimit()
    {
        var (asked, on) = AppHost.Run(() =>
        {
            var editor = new RichTextBox(Document(new string('a', 700_001)));

            var wanted = SpellSupport.Enable(editor, wanted: true, "en-US", maxCharacters: 700_000);

            return (wanted, editor.SpellCheck.IsEnabled);
        });

        Assert.False(asked);
        Assert.False(on);
    }

    [Fact]
    public void SpellCheckGoesOnForADocumentOfAnOrdinarySize()
    {
        var (reported, on, language) = AppHost.Run(() =>
        {
            var editor = new RichTextBox(Document("This sentense has a mispelling in it."));

            var wanted = SpellSupport.Enable(editor, wanted: true, "en-GB");

            return (wanted, editor.SpellCheck.IsEnabled, editor.Language.IetfLanguageTag);
        });

        Assert.True(reported);
        Assert.True(on);
        Assert.Equal("en-gb", language, ignoreCase: true);
    }

    [Fact]
    public void TheLimitIsOffByDefaultInSettings()
    {
        // The guard on the decision above: if this ever defaults to a number,
        // spell check silently stops working on large documents for a reason
        // the measurement does not support.
        Assert.Equal(0, new ScratchpadLite.Core.Platform.Settings().SpellCheckMaxCharacters);
    }

    [Fact]
    public void AskingForItToBeOffTurnsItOff()
    {
        var on = AppHost.Run(() =>
        {
            var editor = new RichTextBox(Document("anything"));

            SpellSupport.Enable(editor, wanted: true, "en-US");
            SpellSupport.Enable(editor, wanted: false, "en-US");

            return editor.SpellCheck.IsEnabled;
        });

        Assert.False(on);
    }

    [Fact]
    public void ThereIsNoErrorToReportWhenSpellCheckIsOff()
    {
        var flagged = AppHost.Run(() =>
        {
            var editor = new RichTextBox(Document("mispelling"));

            SpellSupport.Enable(editor, wanted: false, "en-US");

            return SpellSupport.ErrorAt(editor, editor.Document.ContentStart);
        });

        // The context menu has to be able to ask without checking first.
        Assert.Null(flagged);
    }

    // --------------------------------------------------------------- helpers --

    private static FlowDocument Document(string text) =>
        new(new Paragraph(new Run(text)));

    /// <summary>
    /// Runs the work against a path in its own temporary folder, and takes the
    /// folder away afterwards. The real dictionary lives in the data folder and
    /// a test has no business writing there.
    /// </summary>
    private static T InTempFile<T>(Func<string, T> work)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"splite-lex-{Guid.NewGuid():N}");

        Directory.CreateDirectory(folder);

        try
        {
            return work(Path.Combine(folder, "user-dictionary.lex"));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not a test failure.
            }
        }
    }
}
