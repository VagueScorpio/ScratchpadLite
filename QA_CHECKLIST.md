# Scratchpad Lite — release QA checklist

The twenty-two checks from Section 10.2 of the blueprint, run against the
**v1.10** zip, plus the one Phase 10 added.

Each one says who checked it and how. Three answers are possible:

- **Test** — a test in the suite checks it on every run. The test is named.
- **Run** — checked here against the built `.exe`, with what came back.
- **You** — needs a person, a screen, or hardware this PC is not. These are the
  ones to do before you call the release done.

A check that needs a person is not a weaker check. It is the part a program
cannot see: whether something *looks* right, whether a printed page came out of
a real printer correctly, whether a PC that never had .NET on it runs the file.

---

## 1. A PC that never had .NET installed

> Copy the folder to a Windows PC that never had .NET installed. The .exe
> launches.

**You.** This is the one check in the list that cannot be faked on a developer's
machine, because this machine has the SDK on it and the app would find a runtime
even if it needed one.

What is checked here instead: the publish is self-contained, single-file and
ReadyToRun, and the app runs with every environment variable that could point it
at a shared runtime emptied (`DOTNET_ROOT`, `DOTNET_MULTILEVEL_LOOKUP`) and
`dotnet` taken off the `PATH`. Result below under *What was run here*. That
proves it is not *using* an installed runtime; only a clean PC proves it does not
*need* one.

Take the zip, extract it on the other PC, and run `ScratchpadLite.exe`.

## 2. Startup inside the budget

> The window is ready to type in within 1 second (warm) and 2 seconds (cold,
> after a reboot). The second launch with the full bundle is as fast as a launch
> with an empty `fonts/` folder.

**Run** for warm: **683 ms**, the median of runs 2 to 5, spread 669 to 728 ms.

The same code measured 726, then 692 (over nine runs), then 683. The spread
inside one set of five runs is about 60 ms, which is wider than the differences
between those three numbers, so `build.ps1` now prints the spread beside the
median. A bare number invites a hunt for a regression that is not there.

**You** for cold: it needs a reboot, and a reboot is the only way to empty the
file cache honestly. Reboot, run the `.exe`, and look at the startup line in
`data\logs\` or at Help → About.

## 3. Typing while the fonts are still being prepared

> First launch with the full bundle: you can type at once while the status bar
> shows "Preparing fonts". The bundled fonts appear in the picker as they're
> ready.

**You**, with a full `fonts/` folder. The font bundle is not in the repository
(licences and size), so this needs your own bundle. Drop a few hundred fonts in
`fonts/`, delete `data\font_cache`, and launch.

**Test** for the part that can be tested: `FontPreparerTests` checks that the
pass runs off the UI thread and reports as it goes, and `MainWindowTests` checks
that the window renders its first frame without it.

## 4. SmartScreen

> Windows SmartScreen may warn about an unknown publisher. "More info → Run
> anyway" works. (Document this in the README.)

**You** for the warning itself — it only appears for a file that has been
downloaded, so it needs the zip to come off the internet rather than off this
disk.

**Test**: `ReleaseAuditTests` checks the manifest asks for `asInvoker` and never
for elevation. **Documented**: README.md, *Windows SmartScreen*.

## 5. Every font type in the folder

> Drop a .ttf, .otf, .ttc, .woff, .woff2, a .pfb + .pfm pair, a .dfont, an .eot,
> and a .zip of fonts into `fonts/`. Reload. All appear under "Font folder". Drop
> a .fon and a .vfb. They appear in the Font Report with the reason.

**Test** for every type, against real files: `FontPipelineTests`,
`FontDecoderTests`, `Type1Tests`, `MacAndType42Tests`, `EotDecoderTests`,
`ZipFontSourceTests`, `ConvertedFontTests`. The ones that need a sample file step
aside when `tests/Fixtures/Fonts` is empty, so **run them once with your own
fixtures in place** if you want the full set.

**You** for the Font Report's wording: Tools → Font Report after a reload.

## 6. A converted Type 1 font, on screen and in print

> Type with a converted Type 1 font. It looks right on screen and in print.
> Regular, Bold, and Italic of one Type 1 family appear as one family.

**Test** for everything but the looking: `Type1ConverterTests` (glyph count,
advance widths against the AFM, the cmap, the `.pfm` grouping into one family)
and `ConvertedFontTests` (WPF really loads and draws the result).

**You** for how it looks printed.

## 7. FONT_LICENSES.txt

> Check `FONT_LICENSES.txt`. Every bundled family is listed and any flags make
> sense.

**Run**: `build.ps1` regenerates it from the bundle on every build and prints
anything flagged. **Test**: `FontLicensesTests` checks a font with no licence and
one whose licence forbids redistribution are both flagged.

**You** for whether the flags make sense for your bundle.

## 8. Deleting a font while the app runs

> While the app runs, delete a font from `fonts/`. Windows allows it (proves the
> cache works).

**Test**: `FontRegistryTests.AFontFolderFontBecomesUsableAndResolvesToItsCachedCopy`
— the registry loads the cached copy rather than the file in `fonts/`, which is
exactly what leaves the original deletable while the app is running.

**You** to see Windows allow it.

## 9. Every formatting option, through every format

> Type a paragraph using every formatting option. Save as each format. Reopen
> each. Compare with the fidelity matrix.

**Test**, and this is the largest test in the project:
`EditedContentRoundTripTests` builds a document with every feature the editing
commands can produce and writes and reads it back in every format, comparing
against that format's declared `Capabilities` — which is the same table the
guide's fidelity matrix is printed from.

## 10. A Word .docx and a LibreOffice .odt

> Open a .docx made in Word and an .odt made in LibreOffice. Formatting looks
> right. Save them and reopen in Word / LibreOffice with no repair prompt.

**Test** for the writing side: every `.docx` this app writes passes
`OpenXmlValidator` with zero errors (`DocxTests`), and every `.odt` has its
`mimetype` first and uncompressed with resolving style references (`OdtTests`).
`SampleFilesTests` opens the fixtures from Word, WordPad and LibreOffice.

**You** for the last mile: open a saved file in Word or LibreOffice and check it
does not offer to repair it. A validator is not Word.

## 11. A .doc file

> Open a .doc. It shows the "not supported" message and nothing breaks.

**Test**: `FormatTests` checks that the registry knows a `.doc` is a Word file
and offers no handler for it, which is what makes the window say so rather than
try to read it.

## 12. A missing font

> Open a .docx that uses Helvetica. It shows in Arial. Saving keeps "Helvetica".

**Test**: `SubstitutionsTests` maps Helvetica to Arial, and
`FontRegistryTests.AMissingFontWithASubstituteIsNotReportedAsMissing` checks the
substitution is not treated as a missing font. The name on the run is the
document's own — `BridgeRoundTripTests` checks a family name survives being
built into the editor and walked back out, which is what puts "Helvetica" back
in the saved file.

## 13. Tabs and undo

> Switch tabs back and forth. Undo still works in each tab.

**Test**: `TabTests`, including the specific trap Section 6.12 names — a shared
undo stack losing a tab's history on a switch.

## 14. End the app and recover

> End the app in Task Manager with unsaved changes. Relaunch. Recovery offers the
> document.

**Test**: `RecoveryTests` kills a real process holding a real lock file and
checks the folder it left behind is then offered back.

**You** for the real thing, because Task Manager's End Task is the event this
feature exists for. Type in two tabs, end the task, relaunch.

## 15. AutoSave

> AutoSave is off on first run. Turn it on from the status bar. Edit a saved
> .docx, wait past the interval, and check the file on disk changed. Undo still
> works. An untitled document and a Markdown file with bold text are skipped,
> with the hover note explaining why.

**Test**: `AutoSaveTests` covers off-by-default, each of the four skip rules with
a case that is fine beside each one, the idle pause, and the undo history
surviving a save.

**You** for the switch, the hover note and the file on disk.

## 16. Spell check

> Spell check flags a misspelled word. Add to Dictionary works and survives a
> restart.

**Test**: `SpellCheckTests` covers the dictionary file's format and that a word
added to it is there on the next run.

**You** for the red underline and the right-click menu.

## 17. Dark theme

> Dark theme: menus, dialogs, title bar, and font picker are all readable.

**Test**: `ThemeTests` checks every brush key the themes use is defined in both,
so nothing falls back to a default that disappears on a dark background.

**You** for readable. Contrast is a judgement.

## 18. Print and PDF

> Print a 3-page document. It prints in one column with the right margins. Export
> as PDF produces the same.

**Test**: `PrintingTests` lays out a real page and counts where the glyphs landed
— one column, proved from the page rather than from the property that was set.

**You** for a real printer and real margins.

## 19. Opening a file into a running copy

> With the app running, drag a .docx onto the .exe (or run
> `ScratchpadLite.exe file.docx`). It opens in the running copy as a new tab.

**Test**: `SingleInstanceTests` sends real paths over the real named pipe.

**Run**: see *What was run here*.

## 20. Display scaling

> Test at 100%, 150%, and 200% display scaling. Nothing is blurry or cut off.

**Test**: `ReleaseAuditTests` checks the manifest claims Per-Monitor V2 DPI
awareness in both the 2016 and the 2005 spelling. Without that claim Windows
scales the window as a bitmap and every letter in it goes soft — which is this
check failing for one missing line.

**Run**: the editor was rendered at 96 and 192 DPI and the output compared;
the text and the formatting marks are drawn at the higher resolution rather than
scaled up.

**You** for the rest: change the scaling in Windows Settings while the app is
open, on more than one monitor if you have them, and look at the toolbar, the
status bar, the font picker and the dialogs.

## 21. A USB stick

> Copy the folder to a USB stick and run it there. Settings travel with it.

**You** — this needs a stick. The portable-path logic is `AppPathsTests`, and
the single-instance names carry a hash of the program folder
(`SingleInstanceTests`), so a copy on a stick and a copy in Documents are
separate applications rather than one fighting over a mutex.

## 22. A read-only program folder

> Put the folder somewhere read-only. The app runs and shows the portable-mode
> notice.

**Run**: see *What was run here*.

## 23. A document that carries its own font (v1.10)

> A .docx with a `fonts/` font opens in Word on another PC and shows that font.

**You**, and this one cannot be delegated to a test at all: it asks what Word
does, on a PC that does not have the font. Put a font in `fonts/`, turn on
Preferences → Saving → *Embed fonts from the font folder when saving*, write a
line in that font, save as `.docx`, and open it on the other PC.

**Test** for everything Word would reject the file over: `DocxEmbeddingTests`
checks the obfuscation, the schema, the order of the four faces, the font key
in the XML and the `w:embedTrueTypeFonts` flag — and the file passes
`OpenXmlValidator` with the fonts in it. `FontObfuscationTests` holds the
obfuscation against Microsoft's own implementation, which is the thing Word's
reader agrees with. `OdtEmbeddingTests` does the same for `.odt`, where the
equivalent question is whether LibreOffice shows the font.

**Also worth your eyes:** the same document opened back in Scratchpad Lite on
a PC without the font — it should look right, and the font should *not* appear
in the font picker, because it belongs to that document rather than to the PC.

---

## What was run here

These were run against the published `dist\ScratchpadLite` folder on this PC.
The commands are in the v1.9 entry of `CHANGELOG.txt`; what they produced is
below.

| Check | What was done | Result |
| --- | --- | --- |
| 1. No runtime on the `PATH` | Ran `ScratchpadLite.exe --measure-startup` with `DOTNET_ROOT` pointed at a folder that does not exist, `DOTNET_MULTILEVEL_LOOKUP=0`, and every `dotnet` entry stripped out of `PATH` | Launched and reached its first frame in **677 ms** |
| 19. Hand-off to a running copy | Started the app, waited for it to be listening, then ran the `.exe` again with a file path | The second copy **exited on its own**, one process was left, and the first copy's log reads `opened handed-over.txt (Plain Text, 1 blocks, 0 warnings)` |
| 20. Display scaling | Rendered the editor to a bitmap at 96 and 192 DPI and compared them | Text, pilcrows, space dots and the tab arrow are all **drawn at the higher resolution**, not scaled up from the lower one |
| 22. Read-only program folder | Copied the folder, denied `FILE_WRITE_DATA` and `FILE_ADD_SUBDIRECTORY` on it with `icacls`, then ran the `.exe` | Ran in **686 ms**, created **nothing** beside the `.exe`, and put settings, styles, logs, the font cache and the recovery folder in `%LOCALAPPDATA%\ScratchpadLite` |

One note on check 22, for anyone repeating it. `icacls /deny "user:(W)"` is the
obvious way to make a folder read-only and the wrong one: icacls's `(W)`
includes `SYNCHRONIZE`, and denying that on the `.exe` stops Windows opening
the file at all — the app does not fail to write, it fails to start, which is
not the thing being tested. Denying `(WD,AD)` on the folder alone leaves
everything in it readable and runnable and stops anything new being made.

## What the suite covers

`dotnet test ScratchpadLite.slnx` — **987 tests, 456 in Core and 531 in App**,
all passing in Release for this build.

The tests named in this document are the ones that bear on a checklist item.
They are not the whole suite.
