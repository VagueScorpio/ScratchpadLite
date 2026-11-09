# Scratchpad Lite

A portable Windows word processor. Unzip a folder, run one `.exe`. No installer,
no .NET runtime, no Office, nothing else needed on the PC.

Windows 10 (version 1809 or later) and Windows 11, x64.

> **Status: Phase 1 — editor core.**
> You can type and format: all the character formatting, the paragraph styles,
> alignment, indents and spacing, the pickers, both toolbar rows, the status bar,
> zoom and the page view. Files, fonts from the `fonts/` folder, tabs, find and
> spell check are still ahead — [Build Phases](#build-phases) tracks what lands
> when.

## What it will do

- **Character formatting** — bold, italic, five underline kinds (single, double,
  dotted, dashed, wavy), strikethrough, superscript, subscript, small caps, text
  color, highlight color, and Change Case.
- **Paragraph formatting** — styles (Normal, Title, Subtitle, Heading 1–6, Quote,
  Code), alignment, indents, line spacing, space before and after.
- **Font sizes** from a list or typed freely, half sizes such as 10.5 included.
- **Every font on the PC**, plus a bundled collection, plus anything you drop in
  the `fonts/` folder next to the `.exe` — TrueType, OpenType, WOFF, WOFF2,
  Adobe Type 1, Mac containers, and fonts inside a `.zip`.
- **Lists, tables, images, hyperlinks, page breaks, horizontal rules.**
- **Find and replace, spell check, word count.**
- **Several documents at once** in tabs.
- **Light, dark and follow-Windows themes.**
- **AutoSave to the real file** (a visible switch, off by default) and always-on
  crash recovery.
- **Open and save** `.txt`, `.docx`, `.md`, `.rtf`, `.odt`.
- **Print and export to PDF.**
- **Opens fast** — ready to type in about a second.

## Running it

Download the release zip, extract it, and run `ScratchpadLite.exe`.

Everything the app writes — settings, logs, the font cache, recovery files and
your dictionary — goes into a `data/` folder next to the `.exe`, so the whole
thing travels on a USB stick. If the program folder is read-only, settings fall
back to `%LOCALAPPDATA%\ScratchpadLite` and the app says so once.

### Windows SmartScreen

The `.exe` is not code-signed, so the first run of a freshly downloaded copy
shows **"Windows protected your PC"**. Choose **More info → Run anyway**.
Ticking **Unblock** in the zip's Properties before extracting avoids it
altogether. Windows Defender may also scan the `.exe` on its first run, which
can make that one launch slower.

## Building it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Nothing else — no Python, no Node.

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

That runs the tests, publishes a self-contained single-file `.exe`, copies in
the font bundle and the documents, measures startup five times, zips the result
and prints the sizes and the SHA-256.

Useful switches:

| Switch | What it does |
| --- | --- |
| `-SkipTests` | Publish without running the tests first. Never at a phase gate. |
| `-MeasureVariants` | Also run the three-way publish comparison (compression and native-library layout) and report startup and size for each. |

Day to day:

```powershell
dotnet build ScratchpadLite.slnx
```

```powershell
dotnet test ScratchpadLite.slnx
```

## How it is put together

Think of it as a warehouse with loading docks.

- The **warehouse floor** is the WPF `FlowDocument` — what you see and edit.
- The **shipping pallet** is the **IR**: plain C# records, Document → Paragraphs
  → Runs. Formats convert to and from the pallet and nothing else.
- Each **loading dock** is one file format. It only speaks its own language and
  never touches the floor.
- The **forklift** is two helpers: `FlowBuilder` moves a pallet onto the floor,
  `FlowWalker` packs the floor back onto a pallet.

That split is what makes two things possible. A `FlowDocument` belongs to the UI
thread and cannot be built in the background, but the IR can — so a large file
is parsed off-thread and only the final step runs on the UI thread, and the
window never freezes. And because format code contains no WPF, it lives in a
plain class library and is tested without ever opening a window.

```
src/ScratchpadLite.App     WPF, net10.0-windows — UI, editor, services, bridge
src/ScratchpadLite.Core    class library, net10.0 — model, formats, fonts, platform
tests/                     xUnit; Xunit.StaFact for the WPF side
build/build.ps1            test, publish, bundle, measure, zip
fonts/                     the font bundle; drop font files in here
```

`ScratchpadLite.Core` targets plain `net10.0`, so the compiler itself enforces
that it never references WPF.

### Speed is the first rule

Before the first frame the app does only this: the single-instance check, paths,
settings, the theme, and the window with one empty tab. Everything else —
fonts, recovery, spell check, format libraries — waits until after the window is
on screen and you can already type.

The budget is **1.0 s warm, 2.0 s cold**, measured from process start to the
window's first render. Every launch logs it (`startup: 640 ms`) and Help → About
shows the last value. `ScratchpadLite.exe --measure-startup` prints the number
and exits, which is what the build script takes a median of.

The publish settings were chosen by measurement, not preference
(`build.ps1 -MeasureVariants`). These numbers are from the Phase 0 build — it
is the comparison between the three that settled the choice, and the current
startup figure is in [CHANGELOG.txt](CHANGELOG.txt):

| Publish variant | `.exe` | Startup |
| --- | --- | --- |
| Compression off, native libraries inside the `.exe` — **in use** | 141 MB | **524 ms** |
| Compression off, native libraries beside the `.exe` | 133 MB | 559 ms |
| Compression on | 64 MB | 746 ms |

Compression would cut the `.exe` by more than half and cost 222 ms, which is 42%
of the budget. The shipped zip comes out at 58 MB either way, because the zip
compresses the uncompressed `.exe` about as well as the single-file packer does —
so compression buys nothing a user would notice and costs something they would.

## Build phases

| Phase | Version | Scope |
| --- | --- | --- |
| 0 | v1.0 | Skeleton and pipeline — **done** |
| 1 | v1.1 | Editor core: all formatting, styles, toolbars, shortcuts, status bar — **done** |
| 2 | v1.2 | Fonts: index, cache, WOFF and WOFF2, font picker, Font Report |
| 3 | v1.3 | More font formats: Type 1, Mac containers, EOT, Type 42 |
| 4 | v1.4 | IR, the bridge, TXT and DOCX |
| 5 | v1.5 | Markdown, ODT, RTF |
| 6 | v1.6 | Lists, tables, images, links |
| 7 | v1.7 | Tabs, find and replace, spell check |
| 8 | v1.8 | Recovery, AutoSave, themes, print, single instance |
| 9 | v1.9 | Release: clean-PC test, documents, full QA |
| 10 | v1.10 | Optional: embed fonts in saved documents |

Each phase ends with tests passing, a version bump in `Directory.Build.props`, a
`CHANGELOG.txt` entry, and a measured startup time.

## Documents

- [USER_GUIDE.md](USER_GUIDE.md) — every feature, the `fonts/` folder, AutoSave
  versus recovery, what survives each file format, and the known limitations
- [CHANGELOG.txt](CHANGELOG.txt) — every version, with what changed and why
- [THIRD_PARTY_LICENSES.txt](THIRD_PARTY_LICENSES.txt) — the packages used
- [FONT_LICENSES.txt](FONT_LICENSES.txt) — the bundled fonts
