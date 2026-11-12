# Scratchpad Lite — User Guide

> **Phase 0.** This guide is written out in full so each phase fills in its own
> section rather than inventing the documentation at the end. Anything marked
> **(not yet)** is not built yet — see the phase table in the
> [README](README.md).

## Getting started

Extract the zip anywhere and run `ScratchpadLite.exe`. There is no installer and
nothing to set up.

The window title shows the file you are editing: `report.docx — Scratchpad Lite`,
or `Untitled 1 — Scratchpad Lite` for something new.

### The first run

Windows SmartScreen warns about a program it has not seen before, because this
`.exe` is not code-signed. Choose **More info → Run anyway**. To avoid the
warning entirely, open the zip's **Properties** before extracting and tick
**Unblock**.

Windows Defender may scan the `.exe` the first time it runs, which can make that
one launch noticeably slower than the ones after it.

### Where your settings live

Everything the app writes goes in a `data/` folder next to the `.exe`:

| | |
| --- | --- |
| `settings.json` | your preferences |
| `styles.json` | the paragraph styles, which you can edit |
| `user-dictionary.lex` | words you added to the spell checker |
| `recent-files.json` | the Open Recent list |
| `session.json` | the files that were open last time, for Restore last session |
| `font_cache/` | prepared copies of your fonts, and the font index |
| `recovery/` | crash-recovery copies of open documents |
| `logs/` | one rolling log per day, seven days kept |

So the whole thing travels: copy the folder to a USB stick and your settings go
with it.

If the program folder is read-only — in `Program Files`, say, or on a locked-down
PC — the app says so once and keeps its settings in
`%LOCALAPPDATA%\ScratchpadLite` instead. Nothing else changes.

## Formatting text

### Characters

Bold, italic, underline, strikethrough, superscript, subscript, small caps, text
color, highlight color.

Underline comes in five kinds: single, double, dotted, dashed and wavy. Pick one
from the dropdown beside the underline button, or **Format → Underline**.

**Change Case** rewrites the selected text: UPPERCASE, lowercase, Title Case or
Sentence case. It changes the letters themselves, which is why it survives being
saved to any format.

### Paragraphs

Styles — Normal, Title, Subtitle, Heading 1 to 6, Quote and Code — set the whole
look of a paragraph at once. Applying one sets the paragraph's spacing and the
text formatting for that paragraph; anything you changed by hand on top of it
stays, the way it does in Word. **Clear Formatting** (Ctrl+Space) throws away
the by-hand changes and leaves the style.

Alignment, indents, first-line indent, line spacing and space before and after
are on the toolbar, in the **Format** menu, and together in **Format →
Paragraph**.

You can edit the styles yourself: `data/styles.json` holds them in plain text.

### Font sizes

Pick from the list or type any size you like, half sizes included — `10.5` works.

## Lists, tables, pictures and links

### Lists

The toolbar's two list buttons, **Format → Bulleted List** (Ctrl+Shift+8) and
**Numbered List** (Ctrl+Shift+7), turn the paragraphs you have selected into a
list, and turn a list back into ordinary text.

Inside a list:

- **Tab** makes the item one level deeper, **Shift+Tab** one level shallower.
  Nine levels are available, which is as many as a saved file can describe.
- **Enter** on an empty item ends the list. On an empty item inside a nested
  list it steps out one level instead, so Enter walks back up a nested list
  rather than jumping to the body text in one go.
- Bullets change shape with depth — •, then ◦, then ▪ — and numbered levels go
  1., then a., then i. A nested list is readable instead of being a wall of
  identical dots.

**Restart Numbering** and **Continue Numbering** are in the Format menu and in
the right-click menu. Restarting makes the item you are on the first of a new
list; continuing joins it back onto the list above.

### Tables

**Insert → Table...** asks for rows and columns and whether to draw borders.

With the caret in a table, the **Table** menu and the right-click menu offer
insert row above or below, insert column left or right, delete row, column or
table, merge and split cells, cell shading and borders on or off. Outside a
table every one of them is greyed out.

- **Tab** moves to the next cell and selects what is in it. Tab in the last cell
  adds a row, so a table can be filled in without reaching for the menu.
  **Shift+Tab** goes back.
- **Merging** needs more than one cell selected: drag across them, then
  **Table → Merge Cells**. The text of every cell being merged is kept — a
  merge never throws writing away, even when what it leaves is a cell with
  several paragraphs in it.
- **Splitting** undoes a merge: the cell shrinks back to one and empty cells
  fill the space it gave up.
- Deleting the last row, or the last column, deletes the table.

### Pictures

**Insert → Image...** takes PNG, JPEG, GIF, BMP, TIFF and WebP. A picture wider
than the text column is scaled down to fit, keeping its shape.

Click a picture and four handles appear at its corners. Drag one to resize it;
the shape is kept unless you hold **Shift**. **Image Properties...** in the
right-click menu sets the width, height and alt text exactly, and **Original
Size** puts it back to the picture's own size.

Two things worth knowing:

- **A saved picture is the file you inserted, byte for byte.** Scratchpad Lite
  keeps the original bytes rather than re-encoding on each save, so a photograph
  saved ten times is not ten generations of JPEG.
- **The same picture used twice is stored once.** A logo in forty places costs
  one copy.

WebP needs a Windows codec that is built into Windows 11 and recent Windows 10,
but not every Windows 10 PC has it. If a `.webp` will not open, that is why, and
the message says so.

### Links

**Ctrl+K** inserts a link, or edits the one at the caret. Type the address
without `https://` if you like — a web address gets it added. Selected text
becomes the link's text and keeps its own formatting, so linking a bold word
leaves it bold.

**Ctrl+click** opens a link. A plain click puts the caret in it, because the
text of a link is text and has to be editable.

Web and email addresses — `http`, `https` and `mailto` — open straight away.
**Anything else shows you the full address and asks first**, because a link in a
document can say one thing and point somewhere else entirely, and some addresses
start other programs.

**Remove Link** in the right-click menu takes the link off and keeps the text.

## Fonts

The font picker shows every font Windows has, plus the bundled collection, plus
anything you put in the `fonts/` folder next to the `.exe`. The list is in three
groups — **Font folder** first, then **Recently used**, then **All fonts** —
because a font you put there yourself is the one you are looking for.

### Adding your own fonts

Drop font files into `fonts/`. Subfolders are fine. Zip files of fonts are fine.
Then **Tools → Reload Fonts**, or restart.

Loaded directly:

`.ttf` `.otf` `.ttc` `.otc`

Converted once and then cached, so it only ever costs you the first time:

`.woff` `.woff2` `.pfb` `.pfa` `.dfont` `._*` (Mac AppleDouble) `.eot` `.t42`

Fonts inside a `.zip` are read straight out of the archive — nothing is
unpacked into `fonts/`. A `.pfb` or `.pfa` with its `.pfm` or `.afm` beside it
is handled as a pair, inside a zip as well as on disk.

If a Type 1 font (`.pfb` or `.pfa`) has a `.pfm` or `.afm` file beside it, keep
them together — those carry the metrics and the proper Windows font names, which
is what makes Regular, Bold and Italic group into one family.

Cannot be used: bitmap fonts (`.fon`, `.fnt`, `.bdf`, `.pcf`), SVG fonts,
Multiple Master and CID Type 1 fonts, compressed `.eot` files, and Mac fonts
that lost their data when they were copied to Windows.

**Tools → Font Report** lists every file in the folder and what happened to it,
including the reason for anything that could not be used.

### The first launch after adding a lot of fonts

You can type straight away. Preparing happens in the background and the status
bar shows its progress; the new fonts appear in the picker over the next
moments. Later launches skip all of it — a font that has not changed is never
read again.

### Fonts a document asks for but your PC does not have

The app shows the closest font it does have and tells you, in a bar at the top,
which fonts were substituted. The document keeps the original font name, so
saving it does not lose what it asked for. Someone who opens the file on a PC
that has that font sees it properly.

Going the other way, a font from your `fonts/` folder does **not** travel with a
document unless font embedding is on (Phase 10), so on another PC it falls back
to something else.

## Files

### What opens and saves

`.docx`, `.odt`, `.rtf`, `.md` and `.txt` all open and save.

**Old `.doc` files are not supported.** Open one in Word or LibreOffice and save
it as `.docx` first. Scratchpad Lite will tell you this rather than trying and
getting it wrong.

### What survives each format

Saving to a format that cannot hold everything in your document gets you a
warning naming exactly what will be lost, with the option to save as `.docx`
instead. You can switch the warning off per format once you have seen it.

| | DOCX | ODT | RTF | MD | TXT |
| --- | --- | --- | --- | --- | --- |
| Bold, italic | yes | yes | yes | yes | no |
| Strikethrough | yes | yes | yes | yes | no |
| Underline | yes | yes | one kind only | no | no |
| Super/subscript | yes | yes | **no** | no | no |
| Font name, size | yes | yes | yes | no | no |
| Color, highlight | yes | yes | yes | no | no |
| Headings | yes | yes | look only | yes | no |
| Lists | yes | yes | yes | yes | as text |
| Numbering style | yes | yes | yes | numbers only | no |
| Tables | yes | yes | yes | simple | as tabs |
| Merged cells | yes | yes | yes | no | no |
| Images | yes | yes | re-encoded as PNG | sidecar folder | no |
| Links | yes | yes | yes | yes | no |
| Page setup | yes | yes | no | no | no |

Every row above was measured, not assumed. Three of them are worth a word:

**RTF loses super- and subscript.** Scratchpad Lite uses the RTF converter
built into Windows, and that converter has no way to write either one: the text
keeps its smaller size but drops back onto the baseline. Save as `.docx` or
`.odt` if a document needs them.

**RTF keeps only one kind of underline.** A double, dotted, dashed or wavy
underline comes back as a plain one. The underline itself is never lost.

**RTF headings keep their look, not their name.** A heading saved to `.rtf` is
large bold text. On opening one, Scratchpad Lite works out which heading it was
from its size and weight, so a file it saved reopens with its headings intact —
but a heading in a file from another program is only recognised if its
formatting matches one of the style sheet's headings exactly.

**Markdown puts pictures in a folder.** Saving `report.md` with pictures in it
writes them to `report_images/` beside the file. Both have to travel together.
Saving again clears out the pictures Scratchpad Lite wrote last time and leaves
anything else in that folder alone.

### Saving is safe

A save writes to a temporary file first and only replaces your document once the
new content is safely on disk. A save that gets interrupted therefore cannot
leave you with half a file. If **Keep .bak** is on, the previous version is kept
beside it.

## AutoSave and crash recovery

These are two different things, and the difference matters.

Think of recovery as a security camera that keeps the last few minutes of tape.
It never touches your real file; it only keeps a copy in case the power goes
out. AutoSave is an assistant who presses Save for you every couple of minutes.
It does touch your real file.

### Crash recovery — always on

Every two minutes, a private copy of each open document goes into
`data/recovery/`. Untitled documents are included, which is the point: AutoSave
can do nothing for a document with no file name, and that is exactly the
document whose loss hurts most.

Close the app normally and the copies are deleted. If the app is killed, crashes
or the PC loses power, they stay — and the next launch shows **Recover
Documents**, listing what was found with where it came from and when it was
saved. You can restore the ones you tick, restore all of them, throw them away,
or press **Later** and be asked again next time.

A restored document opens as an **unsaved tab**, with a dot on the tab and a
star in the title. Your original file has not been changed. Nothing is written
over it until you save it yourself.

You can also open the dialog yourself from **File → Recover Documents** if you
pressed Later and changed your mind.

The interval is in Preferences, under Saving. The shortest is 15 seconds.

### AutoSave — writes your real file, and starts off

Turn it on with the **AutoSave: Off** switch at the left of the status bar, from
**File → AutoSave**, or in Preferences. It is one setting for the whole app and
it is remembered.

When it is on it checks every two minutes and saves any document that has a file
name and unsaved changes, in the format it was opened as, through the same safe
save a manual Save uses. A save only happens once you have stopped typing for a
couple of seconds, so it never lands mid-word. Afterwards the status bar shows
**Saved 10:42** for a moment and the tab's dot clears. Your undo history is not
affected — saving never clears it.

It leaves alone anything where saving would be a surprise, and says which in the
status bar once rather than on every check:

- **A document you have never saved.** There is no file to save to, and
  inventing a name is not AutoSave's business. Save it once and AutoSave takes
  over from there.
- **A format that would lose something this document uses** — a picture in a
  `.txt`, a text colour in a `.md`. AutoSave never shows the Save Warning
  dialog; it just does not write.
- **A read-only file**, or one another program is holding.
- **A file that changed on disk** since you opened it. Saving would throw away
  somebody else's work, so you are left to decide which version wins.

Every one of those still gets its recovery copies, so nothing is unprotected —
it is just not being written to your own file.

**"Also AutoSave when I switch away from the app"** (Preferences → Saving, on by
default) saves when the window loses focus, which is usually the moment you were
about to want it.

**The `.bak` file with AutoSave on.** Normally Keep `.bak` keeps the previous
version beside each save. With AutoSave on, that would mean a backup of the
state two minutes ago, which is worth nothing — so the `.bak` is written once
per session, from the version as you opened it.

## Finding, spelling, counting

**Find** (Ctrl+F) and **Replace** (Ctrl+H) open a bar under the toolbar rather
than a dialog, so the document stays visible and you can see the match where it
is. The bar counts as you go — "3 of 17" — and F3 and Shift+F3 step through.
Press Enter in the Find box to go to the next match, Shift+Enter for the
previous one, and Escape to put the caret back in the document.

Four options:

- **Match case** — "Cat" finds Cat and not cat.
- **Whole word** — "cat" skips "cathedral" and "bobcat".
- **Regular expression** — the .NET flavour. A pattern that will not compile
  shows its error in the bar; one that takes more than two seconds is stopped
  and says so, rather than hanging the window.
- **All open tabs** — when the active document has no more matches, the search
  moves on to the next tab. A tab that has not loaded its file yet is read
  first, so it cannot report "no matches" in a document nobody has looked at.

**Replace** swaps one match and finds the next. The replacement keeps the
formatting of the first character of what it replaced, so replacing a word in
the middle of a bold sentence leaves it bold. **Replace All** does the lot
inside a single undo: one Ctrl+Z puts every one of them back.

Matches never cross a paragraph, a line break, a table cell or a picture. That
is not a shortcut — a selection spanning two paragraphs cannot be given
replacement text without destroying the break between them, so a match there
would be one that could not be replaced.

**Spell check** (F7) uses the Windows spell checker: red squiggles, and
right-clicking a flagged word offers up to six suggestions, **Ignore All** and
**Add to Dictionary**. Your words go in `data/user-dictionary.lex` and survive
restarts and upgrades; **Tools → Edit User Dictionary** opens that file, and
Windows reloads it next time the app starts.

**Tools → Language** lists the languages the Windows spell checker has on this
PC — asked of Windows rather than guessed, so everything in the list works. More
languages come from Windows Settings → Time & language → Language.

Paragraphs in the **Code** style are not checked. Code is not prose, and a page
of it underlined in red tells you nothing.

Spell check stays on whatever the size of the document. That was measured
rather than assumed: in a 300-page document a keystroke settles in 18.7 ms with
spell check on and 18.9 ms with it off, so the Windows checker is not what
makes a long document slow — WPF's rich text control is. There is a
`spellCheckMaxCharacters` setting in `settings.json` that leaves spell check off
above a size you choose, and it is zero (no limit) by default, because switching
it off would cost you the squiggles and save you nothing.

**Word Count** is in the Tools menu, and the running count sits in the status
bar.

## Tabs

Several documents are open at once in tabs. Ctrl+N opens one, Ctrl+W closes one,
Ctrl+Tab and Ctrl+Shift+Tab move between them. The **+** at the right of the
strip opens a tab, middle-clicking a tab closes it, and dragging a tab sideways
moves it.

A tab shows a dot when it has unsaved changes, and its tooltip is the full path
— which is the only way to tell two files with the same name apart. Opening a
file that is already open switches to its tab instead of opening a second copy
of it, and the first file you open goes into the empty Untitled tab rather than
beside it.

Each tab keeps its own undo history, its own scroll position and its own
document. Switching away and back costs you nothing, because the editor is kept
alive rather than rebuilt.

Closing a tab with unsaved changes asks Save / Don't Save / Cancel. Closing the
window asks about every modified document in **one** list, not one prompt per
tab.

If **Restore last session** is on (it is by default), the files you had open
come back. Only the one you were last using loads immediately; the others show
their names and load the moment you click them, which is what keeps startup
fast. The same goes for files dropped onto the .exe: all of them get a tab, the
first one loads.

## View

Light, dark, or follow Windows. **Dark Paper** is separate from the dark theme:
it darkens the page itself, which some people want and some very much do not.
With the dark theme on and Dark Paper off, the window is dark and the page stays
white, because a page is a page.

**Page Width** shows the document at the width it would print. **Full Width**
uses the whole window. Zoom runs from 50% to 400% — the View menu, the slider in
the status bar, or Ctrl and the mouse wheel. Ctrl+0 resets it.

**Show Formatting Marks** is not built yet. WPF cannot draw paragraph marks and
space dots inside its rich text control without a custom overlay, so the menu
item is there and disabled rather than pretending.

## Printing and PDF

**Page Setup** (File menu) sets the paper — Letter, Legal, A4 or A5 —
the orientation, and the four margins in inches. These belong to the document
rather than to the app, and `.docx` and `.odt` both store them, so the same file
prints the same way on another PC. Changing them counts as an edit, so the
document will offer to save.

**Print** (Ctrl+P, or the printer button on the toolbar) opens the normal
Windows print dialog and prints a copy laid out for your printer's paper and
your margins. The document on your screen is not disturbed: it is not reflowed
and then put back, so your scroll position, your selection and your undo
history are all where you left them.

**Export as PDF** prints the same copy to **Microsoft Print to PDF**, the
printer that ships with Windows 10 and 11, and Windows asks where to put the
file. There is nothing extra to install. If that printer has been removed —
it can be switched off in Windows Features — the app says so and tells you how
to switch it back on; you can also use **Print** and pick any PDF printer you
have.

Both come out in one column of text per page, which sounds obvious and is the
single easiest thing to get wrong with the rich text control this is built on.

## Preferences

**Tools → Preferences** has every setting in one place, in five tabs:

| Tab | What is in it |
| --- | --- |
| Appearance | theme, dark paper, page or full width, which bars to show |
| Typing | the font, size and paragraph style new documents start with |
| Spelling | spell check on or off, the language, and the long-document limit |
| Saving | the recovery interval, AutoSave and its interval, Keep `.bak` |
| Files | reopen last session, recent files, one window, `.txt` byte-order mark, the size above which a file is refused |

Settings are written half a second after you change something, and again when
you exit, so a crash does not cost you your preferences.

A few of these have a second home where you are more likely to want them: the
theme and the view settings are in the **View** menu, and AutoSave is on the
status bar.

## One window, or several

By default, opening a document while Scratchpad Lite is already running sends it
to the window that is already open — a new tab there, and that window comes to
the front. Dropping four files on the `.exe` opens four tabs in one window.

Turn **One window for everything** off in Preferences → Files if you would
rather each launch be its own window.

Two copies in two different folders — one on a USB stick and one in your
Documents, say — are always separate applications and never hand files to each
other.

Nothing about this is written to the Windows registry. To make Scratchpad Lite
the program that opens `.docx` files, use Windows' own **Open with → Choose
another app**.

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Bold / Italic / Underline | Ctrl+B / Ctrl+I / Ctrl+U |
| Strikethrough | Alt+Shift+5 |
| Superscript / Subscript | Ctrl+Shift+= / Ctrl+= |
| Heading 1–6 | Ctrl+Alt+1 … Ctrl+Alt+6 |
| Normal style | Ctrl+Alt+0 |
| Grow / Shrink font | Ctrl+Shift+> / Ctrl+Shift+< |
| Align left / centre / right / justify | Ctrl+L / Ctrl+E / Ctrl+R / Ctrl+J |
| Bulleted / Numbered list | Ctrl+Shift+8 / Ctrl+Shift+7 |
| Clear formatting | Ctrl+Space |
| Insert or edit a link | Ctrl+K |
| Deeper / shallower list level | Tab / Shift+Tab |
| Next / previous table cell | Tab / Shift+Tab |
| Open the link at the caret | Ctrl+click |
| Paste as plain text | Ctrl+Shift+V |
| Find / Replace | Ctrl+F / Ctrl+H |
| Find next / previous | F3 / Shift+F3 |
| Spell check on/off | F7 |
| New tab / Open / Save / Save As | Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| Close tab | Ctrl+W |
| Next / previous tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Next / previous match, in the Find box | Enter / Shift+Enter |
| Close the find bar | Escape |
| Print | Ctrl+P |
| Reset zoom | Ctrl+0 |

## Command line

```
ScratchpadLite.exe [files...] [--fonts <path>] [--debug]
```

Files open as tabs. If a copy is already running, the files open in that copy
instead of starting a second one — which is also what happens when you drag a
document onto the `.exe`.

`--fonts <path>` uses a different font folder; a relative path is taken from the
program folder. `--debug` turns on detailed logging.

There is also `--measure-startup`, which renders the window once, prints how long
it took, and exits. It is there for the build script.

## Known limitations

1. **Old `.doc` files are not supported.** Convert to `.docx` first.
2. **Some font types cannot be used:** bitmap fonts (`.fon`, `.fnt`, `.bdf`,
   `.pcf`), SVG fonts, Multiple Master and CID Type 1 fonts, compressed `.eot`
   files, and Mac fonts that lost their data on the way to Windows. The Font
   Report gives the reason for each file.
3. **Converted Type 1 fonts lose their hinting.** They can look slightly softer
   on screen at small sizes. Printing is unaffected. They are not embedded in
   saved documents.
4. **The first launch after adding many fonts** prepares them in the background.
   You can type at once, but those fonts appear in the picker over the next
   moments. Later launches skip the work.
5. **Variable fonts** may show only their default style. Confirmed in Phase 2;
   this line gets the real answer then.
6. **A font removed from `fonts/` stays in the list until you restart.** Windows
   cannot unload a font that is in use.
7. **Custom fonts travel only if embedded.** Without embedding (Phase 10), a
   document using a `fonts/` font falls back to another font elsewhere.
8. **Gaps in WPF's text engine:** no all-caps character format (use Change Case,
   which changes the letters), no letter spacing, small caps only on fonts that
   support it, and wavy underline is drawn approximately. A file that already
   uses all-caps or letter spacing keeps them when you save it back.
9. **Markdown and plain text lose formatting.** The save warning says what.
10. **RTF goes through WPF's own converter.** Paragraph style names are not kept
    and some features are best effort.
11. **A table selection is a stretch of the document, not a rectangle.** Drag
    across cells and WPF widens the selection to whole rows. Scratchpad Lite
    works out what you really swept from the cells the mouse went between, so
    merging a column works — but selecting cells with the keyboard and merging
    them merges the whole block between them.
12. **Find and replace stops at a paragraph.** A match cannot span two
    paragraphs, a line break, a table cell boundary or a picture, because a
    replacement across one of those would destroy the break it crossed.
13. **Replacing the text of a link removes the link.** Replace All over a word
    that happens to be inside a hyperlink leaves the new word as plain text.
    Use Ctrl+K to put the link back.
14. **Not a page-layout program.** The editor shows one continuous page-width
    column; pages are only split for printing and PDF. Headers, footers, columns
    and footnotes are out of scope for version 1.
15. **Very large documents get slower,** because WPF's rich text control is not
    built for book-length files. Measured: a keystroke settles in about 3 ms at
    20 pages, 6 ms at 100, 19 ms at 300 and 90 ms at 600. Spell check is not
    the cause — the same numbers come out with it off.
16. **PDF export cannot be given a file name.** It prints to Microsoft Print to
    PDF, and that printer shows Windows' own Save As dialog — there is no way to
    pass a path through to it, so the app lets Windows ask rather than asking
    you twice.
17. **AutoSave will not save a document you have never saved.** Save it once
    and AutoSave looks after it from then on. Crash recovery covers it in the
    meantime.
18. **A recovered document is not your file.** Restoring from the Recover
    Documents dialog opens the work as an unsaved tab; you decide whether it
    replaces what is on disk.
19. **Two menu items are not built yet:** Format → Paragraph (and Line Spacing
    → Custom), and Insert → Special Character. They are there and disabled
    rather than missing. Line spacing is available from its own submenu, and
    indents from the Format menu.
20. **The `.exe` is not signed,** so SmartScreen warns on first run.
