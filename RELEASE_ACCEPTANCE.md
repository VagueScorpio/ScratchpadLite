# Release acceptance — one sitting, one other PC

`QA_CHECKLIST.md` says which of the twenty-three checks a test already covers
and which need a person. This is the person's half, put in an order that closes
the most of it in one pass, on one Windows PC that is not the one the app was
built on.

Seventeen of the twenty-three checks have a part only you can do. **Eleven of
them close in this one sitting.** The rest need a printer, a USB stick, or a
font bundle, and they are at the end.

The order is not arbitrary. Two checks can only happen once each:

- **SmartScreen** fires on a file that was *downloaded*. Copy the zip over on a
  stick and you have spent that check. Download it on the other PC.
- **Cold startup** means the file cache is empty, which only a reboot gives you
  honestly. So: download, extract, reboot, *then* launch for the first time.

Get those two wrong and you cannot retake them without another PC or another
reboot.

---

## Before you leave this PC

### 1. The embedded-font document is already built

`tests/Fixtures/Docs/embedded-font-test.docx` and `.odt`. These were written by
the real save path, and they carry a font called **Scratchpad Embed Test** —
Wingdings redrawn under a name no PC has.

That makes check 23 answerable at a glance instead of by measurement:

| What you see in Word | What it means |
| --- | --- |
| Symbols — stars, arrows, boxes | The embedded font loaded. **Pass.** |
| Ordinary letters | Word substituted. **Fail.** |

Copy both files to the stick.

### 2. A font in `fonts/`, if you want the font checks

`fonts/` holds only its README right now, and `FONT_LICENSES.txt` says as much.
Checks 3, 5, 7 and the authoring half of 23 are about behaviour with real fonts
in that folder, and none of them can be done until there are some. Drop in a
few hundred if you have them, or a handful if you do not — a `.ttf`, an `.otf`,
a `.woff2`, a `.pfb`+`.pfm` pair and a `.zip` of fonts covers check 5's list.

### 3. Files for check 10

Two the app wrote, for opening in Word and LibreOffice:

```powershell
dotnet test tests/ScratchpadLite.Core.Tests --filter WriteSamplesForTheHandTest
```

They land in
`tests/ScratchpadLite.Core.Tests/bin/Debug/net10.0/samples/` as
`Phase5-sample.odt` and `Phase5-sample.md`. Copy them to the stick.

Also bring **one `.docx` made in Word and one `.odt` made in LibreOffice** —
anything real, with headings, a list, a table and an image. Check 10 is the one
place where no test helps you at all (see the note at the bottom), so the files
have to come from the other programs rather than from this one.

### 4. On the stick

```
embedded-font-test.docx
embedded-font-test.odt
Phase5-sample.odt
Phase5-sample.md
your-word-file.docx
your-libreoffice-file.odt
ScratchpadLite-v1.10-windows-x64.zip     (for check 21 only - see step 11)
```

---

## The sitting

### Step 1 — Download it there · *prep for check 4*

On the other PC, in a browser:

https://github.com/VagueScorpio/ScratchpadLite/releases/tag/v1.10

Download `ScratchpadLite-v1.10-windows-x64.zip`. **Do not tick Unblock**, and do
not copy it from the stick — both of those skip the check.

Extract it. Right-click → Extract All is fine.

### Step 2 — Reboot · *prep for check 2*

Now, before launching anything. This is the only way to measure cold startup
honestly.

### Step 3 — First launch · *closes 1, 2, 4*

Run `ScratchpadLite.exe`.

- **Check 4 — SmartScreen.** Expect "Windows protected your PC". **Pass:** More
  info → Run anyway opens the app. **Fail:** no way through, or it demands an
  administrator.
- **Check 1 — no .NET.** **Pass:** the window opens and you can type. **Fail:**
  any dialog about a missing runtime or framework. *This is the check the whole
  product rests on, and it has never been done.*
- **Check 2 — cold startup.** Help → About, or the newest file in `data\logs\`.
  **Pass:** under 2000 ms. Write the number down.

Then confirm it is portable: a `data\` folder next to the `.exe`, and nothing
written anywhere else.

### Step 4 — The embedded font, in Word · *closes 23*

Open `embedded-font-test.docx` **in Word** on this PC.

- **Pass:** no repair prompt, and the line is **symbols** (see the table above).
- **Fail:** a repair prompt, or ordinary letters.

Then File → Save in Word, close, reopen. The symbols should still be there — if
Word drops the font on its own save, `w:embedTrueTypeFonts` is not doing its
job.

If you have LibreOffice here, do the same with the `.odt`.

### Step 5 — The embedded font, back in Scratchpad Lite · *closes 23's second half*

Open the same `.docx` in Scratchpad Lite on this PC.

- **Pass:** the line shows as symbols, **and** "Scratchpad Embed Test" does
  **not** appear in the font picker. It belongs to that document, not to this
  PC.
- **Fail:** letters instead of symbols, or the name showing up in the picker.

### Step 6 — What the other programs wrote · *closes 10*

Open your Word `.docx` and your LibreOffice `.odt` in Scratchpad Lite. Headings,
lists, tables and images should all look right.

Then the other direction, which is the one that catches real faults: save each
one from Scratchpad Lite, and open the saved file back in Word / LibreOffice.

- **Pass:** opens with **no repair prompt**.
- **Fail:** any offer to repair or recover. A validator is not Word, and this
  check exists because of the difference.

Also open `Phase5-sample.odt` in both, and `Phase5-sample.md` anywhere.

### Step 7 — Print, and PDF · *closes 18*

Type or open three pages. Ctrl+P to a real printer.

- **Pass:** one column, margins right, nothing cut off at the edges.
- **Fail:** two columns, or text in the margin.

Then export to PDF and compare. If you put a converted Type 1 font on a line
first, the printed page also closes **check 6**.

### Step 8 — While you are there · *closes 14, 15, 16, 17, 19, 20*

These are quick and need no setup beyond being at the keyboard.

- **14 — Recovery.** Type in two tabs, do not save, End Task in Task Manager,
  relaunch. **Pass:** both documents are offered back.
- **15 — AutoSave.** It should be **off**. Turn it on from the status bar, edit
  a saved `.docx`, wait past the interval, check the file's timestamp moved.
  Hover the indicator on an untitled document — it should say why it is
  skipping.
- **16 — Spell check.** Type `teh`. **Pass:** red underline, right-click
  suggests, Add to Dictionary sticks after a restart.
- **17 — Dark theme.** Switch to it. Read the menus, the dialogs, the title bar
  and the font picker. **Pass:** all readable. Contrast is your judgement, not a
  test's.
- **19 — Hand-off.** With the app open, drag a `.docx` onto the `.exe`. **Pass:**
  it opens as a new tab in the running copy; no second window.
- **20 — Display scaling.** Change scaling in Windows Settings to 125%, 150%,
  200% **while the app is open**. Look at the toolbar, status bar, font picker
  and a dialog. **Pass:** sharp, nothing clipped. On two monitors at different
  scalings, drag the window between them.

### Step 9 — A font, while it is running · *closes 8*

With a font in `fonts/` and the app open, delete that font from the folder.

- **Pass:** Windows lets you. It proves the app is holding its cached copy, not
  the original.
- **Fail:** "the file is in use".

### Step 10 — The Font Report · *closes 5, 7*

Needs the bundle from prep step 2. Tools → Font Report after a reload.

- **5:** every type you dropped in appears under "Font folder", and the ones
  that cannot work (`.fon`, `.vfb`) are listed **with a reason that makes
  sense**.
- **7:** `FONT_LICENSES.txt` lists every family, and the flags are right for
  your fonts. This one is a judgement about your bundle; nobody else can make
  it.

### Step 11 — The stick · *closes 21*

Extract a **second** copy of the app onto the USB stick and run it from there.

- **Pass:** it runs, and its settings live on the stick — change a setting
  there, and the copy on the hard disk should not have it.
- **Fail:** they share settings, or the second copy refuses to start because the
  first one is running. They are meant to be separate applications.

### Step 12 — First launch with the bundle · *closes 3*

Needs the bundle. Delete `data\font_cache` and launch.

- **Pass:** you can type **immediately**, while the status bar still says
  "Preparing fonts", and the bundled fonts appear in the picker as they become
  ready.
- **Fail:** the window is unresponsive until it finishes.

Then close and launch again. **Pass:** as fast as a launch with an empty
`fonts/` folder — that is the Phase 2 promise, and the cache is what keeps it.

---

## Write the results in

| # | Check | Result | Note |
| --- | --- | --- | --- |
| 1 | No .NET on the PC | | |
| 2 | Cold startup | | ms |
| 3 | Typing while fonts prepare | | |
| 4 | SmartScreen | | |
| 5 | Every font type | | |
| 6 | Type 1, printed | | |
| 7 | FONT_LICENSES flags | | |
| 8 | Delete a font while running | | |
| 10 | Word / LibreOffice, no repair | | |
| 14 | End task and recover | | |
| 15 | AutoSave | | |
| 16 | Spell check | | |
| 17 | Dark theme | | |
| 18 | Print and PDF | | |
| 20 | Display scaling | | |
| 21 | USB stick | | |
| 23 | A document carrying its font | | |

Checks 9, 11, 12, 13, 19 and 22 are closed by the suite and by runs already
recorded in `QA_CHECKLIST.md`; 19 is in step 8 anyway because it costs nothing
once you are there.

---

## One thing the checklist got wrong

`QA_CHECKLIST.md` said check 10 was partly covered by `SampleFilesTests`
"opening the fixtures from Word, WordPad and LibreOffice". It is not.
`SampleFilesTests` **writes** two sample files with the real writers and reads
them back; it opens nothing that another program made. And nothing in either
suite reads `tests/Fixtures/Docs/` at all — only the font fixtures are
consumed, which is why that folder has never held anything but its README.

So the "open what Word actually wrote" direction has **no** automated coverage,
in either suite. That is why step 6 asks you to bring real files rather than
relying on the samples: the samples prove this app agrees with itself, which is
the one thing a round trip can never tell you.
