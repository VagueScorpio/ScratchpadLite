using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI;

/// <summary>
/// Find and replace (Section 6.10) and spell check (Section 6.11), from the
/// window's side.
/// </summary>
/// <remarks>
/// Both are in one file because both are the same shape: a piece of logic that
/// can be tested without a window - <see cref="FindEngine"/> and
/// <see cref="SpellSupport"/> - and the part that knows about tabs, menus and
/// the status bar, which is here.
/// </remarks>
public partial class MainWindow
{
    /// <summary>The matches as last computed, and what they were computed from.</summary>
    private IReadOnlyList<TextRange> _matches = [];

    private DocumentSession? _matchesFor;
    private FindEngine.Options? _matchesFrom;

    /// <summary>
    /// Set once the spell checker has been switched on for the first time,
    /// which is after the first frame and never before (Section 2.1).
    /// </summary>
    private bool _spellCheckStarted;

    // ------------------------------------------------- find and replace -----

    private void WireFindBar()
    {
        FindBar.SearchRequested += (_, backwards) => _ = FindAgainAsync(backwards);
        FindBar.ReplaceRequested += (_, _) => ReplaceCurrent();
        FindBar.ReplaceAllRequested += (_, _) => ReplaceEvery();
        FindBar.OptionsChanged += (_, _) => RecountMatches();
        FindBar.CloseRequested += (_, _) => CloseFindBar();
    }

    /// <summary>Ctrl+F and Ctrl+H, which are the same bar in two modes.</summary>
    private void OpenFindBar(bool replacing)
    {
        FindBar.Open(replacing, Rtb.Selection.Text);
        RecountMatches();
    }

    private void CloseFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;

        // The caret has to come back to the document, or Escape leaves the
        // keyboard pointing at a bar that is no longer there.
        Rtb.Focus();
    }

    /// <summary>
    /// Recomputes the matches for the active document and shows the count,
    /// without moving anything.
    /// </summary>
    /// <remarks>
    /// Nothing jumps while the user is still typing in the box. A search box
    /// that scrolls the document on every keystroke makes the next keystroke
    /// land somewhere else, and Section 6.10 asks for the count rather than for
    /// find-as-you-type.
    /// </remarks>
    private void RecountMatches()
    {
        if (FindBar.Visibility != Visibility.Visible)
        {
            return;
        }

        if (!TryFindAll(out var index))
        {
            return;
        }

        FindBar.ShowCount(index ?? 0, _matches.Count);
    }

    /// <summary>
    /// Fills <see cref="_matches"/> for the active document, and reports which
    /// one the caret is in. False means the pattern was bad and the message is
    /// already on the bar.
    /// </summary>
    private bool TryFindAll(out int? current)
    {
        current = null;

        var options = FindBar.Options;

        try
        {
            _matches = FindEngine.All(Rtb.Document, options);
            _matchesFor = _tabs.Active;
            _matchesFrom = options;

            current = FindEngine.IndexOfCurrent(_matches, Rtb.Selection.Start);

            return true;
        }
        catch (FindEngine.BadPatternException ex)
        {
            // Section 6.10: the error shows in the bar, and that is all that
            // happens.
            _matches = [];
            _matchesFor = null;

            FindBar.ShowMessage(ex.Message);

            return false;
        }
    }

    private async Task FindAgainAsync(bool backwards)
    {
        if (FindBar.Options.Needle.Length == 0)
        {
            return;
        }

        if (FindBar.Visibility != Visibility.Visible)
        {
            // F3 with the bar closed searches for whatever was last typed in
            // it, which is what F3 does everywhere.
            FindBar.Open(replacing: false, selected: null);
        }

        if (!TryFindAll(out _))
        {
            return;
        }

        if (_matches.Count == 0)
        {
            if (FindBar.AllTabs && await MoveToATabWithAMatchAsync(backwards).ConfigureAwait(true))
            {
                return;
            }

            FindBar.ShowCount(0, 0);

            return;
        }

        // Forward from the end of the selection and backwards from its start,
        // so Next never finds the match that is already selected.
        var from = backwards ? Rtb.Selection.Start : Rtb.Selection.End;
        var index = FindEngine.IndexFrom(_matches, from, backwards);

        FindEngine.Show(Rtb, _matches[index]);
        FindBar.ShowCount(index, _matches.Count);
    }

    /// <summary>
    /// "Search all open tabs" (Section 6.10): when the active document has no
    /// match, go round the other tabs until one does.
    /// </summary>
    /// <remarks>
    /// A tab that has not loaded its file yet is read first. Skipping it would
    /// be worse than slow: the bar would say there is no match in a document
    /// nobody has looked at.
    /// </remarks>
    private async Task<bool> MoveToATabWithAMatchAsync(bool backwards)
    {
        var tabs = _tabs.Tabs;

        if (tabs.Count < 2 || _tabs.Active is not { } active)
        {
            return false;
        }

        var start = IndexOfTab(tabs, active);

        for (var step = 1; step < tabs.Count; step++)
        {
            var at = ((start + (backwards ? -step : step)) % tabs.Count + tabs.Count) % tabs.Count;
            var session = tabs[at];

            _tabs.Activate(session);
            await LoadPendingAsync(session).ConfigureAwait(true);

            if (!TryFindAll(out _) || _matches.Count == 0)
            {
                continue;
            }

            var index = backwards ? _matches.Count - 1 : 0;

            FindEngine.Show(Rtb, _matches[index]);
            FindBar.ShowCount(index, _matches.Count);

            return true;
        }

        _tabs.Activate(active);

        return false;
    }

    private static int IndexOfTab(IReadOnlyList<DocumentSession> tabs, DocumentSession wanted)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (ReferenceEquals(tabs[i], wanted))
            {
                return i;
            }
        }

        return 0;
    }

    private void ReplaceCurrent()
    {
        if (!TryFindAll(out var current))
        {
            return;
        }

        if (current is not { } index)
        {
            // Nothing selected yet, so Replace means "find one first", which is
            // what every other editor does with the same button.
            _ = FindAgainAsync(backwards: false);

            return;
        }

        FindEngine.Replace(Rtb, _matches[index], FindBar.Replacement);

        // The matches are stale the moment the text changes, so the next one is
        // found from the document as it is now.
        _ = FindAgainAsync(backwards: false);
    }

    private void ReplaceEvery()
    {
        var options = FindBar.Options;

        if (options.Needle.Length == 0)
        {
            return;
        }

        try
        {
            var replaced = FindEngine.ReplaceAll(Rtb, options, FindBar.Replacement);

            _matches = [];
            _matchesFor = null;

            FindBar.ShowMessage(
                replaced switch
                {
                    0 => "No matches.",
                    1 => "1 replaced.",
                    _ => $"{replaced:N0} replaced.",
                });

            FindBar.FocusFindBox();
        }
        catch (FindEngine.BadPatternException ex)
        {
            FindBar.ShowMessage(ex.Message);
        }
    }

    // ------------------------------------------------------ spell check -----

    /// <summary>
    /// Switches the spell checker on for the first time. Called from
    /// <c>ContentRendered</c>, never from the constructor: turning it on loads
    /// the Windows spell checker (Section 2.1).
    /// </summary>
    private void StartSpellCheck()
    {
        _spellCheckStarted = true;

        SpellCheckMenuItem.IsChecked = _settings.SpellCheckEnabled;

        foreach (var session in _tabs.Tabs)
        {
            if (session.View is { } view)
            {
                ApplySpellCheck(view);
            }
        }
    }

    /// <summary>
    /// Applies the current spell-check settings to one editor, including the
    /// size limit (Section 6.11).
    /// </summary>
    private void ApplySpellCheck(EditorView view)
    {
        var on = SpellSupport.Enable(
            view.Editor,
            _settings.SpellCheckEnabled,
            _settings.SpellCheckLanguage,
            _settings.SpellCheckMaxCharacters);

        // Code is not prose, and a page of it underlined in red says nothing.
        SpellSupport.MarkCodeParagraphs(view.Editor.Document);

        if (_settings.SpellCheckEnabled && !on)
        {
            Status.ShowMessage(
                "This document is over the spell-check size limit in settings.json, "
                + "so spell check is off for it.",
                transient: false);
        }

        Status.ShowLanguage(on ? _settings.SpellCheckLanguage : "no spell check");
    }

    private void ToggleSpellCheck()
    {
        Apply(_settings with { SpellCheckEnabled = !_settings.SpellCheckEnabled });

        SpellCheckMenuItem.IsChecked = _settings.SpellCheckEnabled;

        foreach (var session in _tabs.Tabs)
        {
            if (session.View is { } view)
            {
                ApplySpellCheck(view);
            }
        }

        _spellCheckStarted = true;
    }

    private void SetSpellCheckLanguage(string language)
    {
        Apply(_settings with { SpellCheckLanguage = language });

        foreach (var session in _tabs.Tabs)
        {
            if (session.View is { } view)
            {
                ApplySpellCheck(view);
            }
        }
    }

    /// <summary>
    /// Fills Tools, Language when it is opened, from the languages Windows says
    /// its spell checker has (Section 6.11).
    /// </summary>
    /// <remarks>
    /// Built on opening rather than at startup for the usual reason - asking
    /// Windows costs a COM call - and also because a language pack installed
    /// while the app is running then appears without a restart.
    /// </remarks>
    private void OnLanguageMenuOpened(object sender, RoutedEventArgs e)
    {
        LanguageMenu.Items.Clear();

        foreach (var language in SpellSupport.Languages())
        {
            LanguageMenu.Items.Add(new MenuItem
            {
                Header = Describe(language),
                IsCheckable = true,
                IsChecked = string.Equals(
                    language, _settings.SpellCheckLanguage, StringComparison.OrdinalIgnoreCase),
                Command = Commands.SetSpellCheckLanguage,
                CommandParameter = language,
                CommandTarget = Rtb,
            });
        }

        LanguageMenu.Items.Add(new Separator());

        LanguageMenu.Items.Add(new MenuItem
        {
            // Section 6.11 asks for this note: the list is Windows', not this
            // app's, so this is where more languages come from.
            Header = "More languages come from Windows Settings › Time & language › Language",
            IsEnabled = false,
        });
    }

    private static string Describe(string tag)
    {
        try
        {
            return $"{System.Globalization.CultureInfo.GetCultureInfo(tag).DisplayName}  ({tag})";
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return tag;
        }
    }

    /// <summary>
    /// Opens <c>data/user-dictionary.lex</c> in whatever Windows uses for a
    /// text file (Section 6.11).
    /// </summary>
    /// <remarks>
    /// A dialog of its own was the alternative, and it would be a worse one: a
    /// list of words with Add and Remove buttons is a text editor, and this app
    /// is running inside one. What it does do is make sure the file exists
    /// first, so the editor does not open on nothing.
    /// </remarks>
    private void EditUserDictionary()
    {
        var path = AppPaths.UserDictionaryFile;

        try
        {
            if (!File.Exists(path))
            {
                SpellSupport.Write(path, []);
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            Status.ShowMessage(
                "Close and reopen Scratchpad Lite after editing the dictionary, so Windows reloads it.");
        }
        catch (Exception ex)
        {
            Log.Warn($"the user dictionary could not be opened: {ex.Message}");

            MessageBox.Show(
                this,
                $"The dictionary file could not be opened.\n\n{path}",
                "Edit User Dictionary",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Puts the spelling suggestions at the top of the editor's context menu
    /// (Section 6.11), for the word under the pointer.
    /// </summary>
    /// <remarks>
    /// Built as the menu opens, because the suggestions are for one particular
    /// misspelling and are different every time. The items this added last time
    /// are taken off first, which is what the tag is for: the rest of the menu
    /// is the one defined in <c>EditorView.xaml</c> and has to survive.
    /// </remarks>
    private void OnEditorContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not RichTextBox editor || editor.ContextMenu is not { } menu)
        {
            return;
        }

        foreach (var stale in menu.Items.OfType<FrameworkElement>()
                     .Where(i => Equals(i.Tag, SpellingMenuTag))
                     .ToList())
        {
            menu.Items.Remove(stale);
        }

        // The pointer, not the caret: a right-click does not always move the
        // caret, and the word the user means is the one they clicked on.
        var at = editor.GetPositionFromPoint(Mouse.GetPosition(editor), snapToText: true);

        if (SpellSupport.ErrorAt(editor, at) is not { } flagged)
        {
            return;
        }

        var added = new List<FrameworkElement>();

        foreach (var suggestion in flagged.Suggestions)
        {
            var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.SemiBold };
            var word = suggestion;

            item.Click += (_, _) => flagged.Error.Correct(word);
            added.Add(item);
        }

        if (flagged.Suggestions.Count == 0)
        {
            added.Add(new MenuItem { Header = "No suggestions", IsEnabled = false });
        }

        var ignore = new MenuItem { Header = "Ignore All" };
        ignore.Click += (_, _) => flagged.Error.IgnoreAll();
        added.Add(ignore);

        var add = new MenuItem { Header = "Add to Dictionary" };
        add.Click += (_, _) =>
        {
            if (flagged.Word is { Length: > 0 } misspelled)
            {
                SpellSupport.AddToUserDictionary(editor, misspelled);
            }
        };

        added.Add(add);
        added.Add(new Separator());

        for (var i = 0; i < added.Count; i++)
        {
            added[i].Tag = SpellingMenuTag;
            menu.Items.Insert(i, added[i]);
        }
    }

    private const string SpellingMenuTag = "spelling";
}
