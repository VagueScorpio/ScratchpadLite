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

`.docx` and `.txt` work now. `.md`, `.rtf` and `.odt` arrive in Phase 5; until
then the Open dialog does not offer them.

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
| Underline kinds | yes | yes | partly | no | no |
| Strikethrough | yes | yes | yes | yes | no |
| Super/subscript | yes | yes | yes | no | no |
| Font name, size | yes | yes | yes | no | no |
| Color, highlight | yes | yes | yes | no | no |
| Headings | yes | yes | look only | yes | no |
| Lists | yes | yes | yes | yes | as text |
| Tables | yes | yes | partly | simple | as tabs |
| Images | yes | yes | partly | sidecar folder | no |
| Links | yes | yes | partly | yes | no |
| Page setup | yes | yes | no | no | no |

The "partly" entries are confirmed in Phase 5 and this table is updated with the
real results then.

### Saving is safe

A save writes to a temporary file first and only replaces your document once the
new content is safely on disk. A save that gets interrupted therefore cannot
leave you with half a file. If **Keep .bak** is on, the previous version is kept
beside it.

## AutoSave and crash recovery **(not yet — Phase 8)**

These are two different things, and the difference matters.

**Crash recovery is always on.** Every so often the app writes a private copy of
each open document into `data/recovery/`. It never touches your real file. Close
the app normally and the copies are deleted. If the app is killed or the PC
loses power, the next launch finds them and offers your work back.

**AutoSave writes your real file, and starts off.** Turn it on from the status
bar or in Preferences. When it is on, it saves a document that already has a
file name and has unsaved changes, in the format it was opened as. It leaves
alone anything where saving would be a surprise: a document you have never saved,
a format that would lose formatting, a read-only file, or a file that changed on
disk behind your back. Hovering the indicator explains why a document was
skipped.

## Finding, spelling, counting

> Word Count works now. Find, Replace and spell check arrive in Phase 7.

**Find** (Ctrl+F) and **Replace** (Ctrl+H) open a bar rather than a dialog, so
the document stays visible. Match case, whole word and wrap-around are there,
and Replace All counts as one undo.

**Spell check** (F7) uses the Windows spell checker: red squiggles, suggestions
in the right-click menu, and **Add to Dictionary** to keep a word. Your words go
in `data/user-dictionary.lex` and survive restarts and upgrades.

**Word Count** is in the Tools menu, and the running count sits in the status
bar.

## Tabs **(not yet — Phase 7)**

Several documents are open at once in tabs. Ctrl+N opens one, Ctrl+W closes one,
Ctrl+Tab moves between them. Each tab keeps its own undo history, so switching
away and back does not cost you your undo.

If **Restore last session** is on, the tabs you had come back. Only the one you
were last using loads immediately; the others load the moment you click them,
which is what keeps startup fast.

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

## Printing and PDF **(not yet — Phase 8)**

**Print** (Ctrl+P) paginates for the paper size and margins in **Page Setup**.
**Export as PDF** goes through the Microsoft Print to PDF printer built into
Windows, so there is nothing extra to install.

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
| Insert link | Ctrl+K |
| Paste as plain text | Ctrl+Shift+V |
| Find / Replace | Ctrl+F / Ctrl+H |
| Find next / previous | F3 / Shift+F3 |
| Spell check on/off | F7 |
| New tab / Open / Save / Save As | Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| Close tab / Next tab | Ctrl+W / Ctrl+Tab |
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
11. **Not a page-layout program.** The editor shows one continuous page-width
    column; pages are only split for printing and PDF. Headers, footers, columns
    and footnotes are out of scope for version 1.
12. **Very large documents** — hundreds of pages — get slower, because WPF's rich
    text control is not built for book-length files.
13. **The `.exe` is not signed,** so SmartScreen warns on first run.
