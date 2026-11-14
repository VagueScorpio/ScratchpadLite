# Scratchpad Lite — notes for a Claude Code session

A portable Windows word processor: C# / WPF / .NET 10, shipping as one
self-contained `.exe`. Finished through Phase 10; `v1.10` is released.

Read these before changing anything:

| What | Where |
|---|---|
| The spec this is built to | `docs/Scratchpad-Lite-Blueprint-v1.3-CSharp-Windows.md` |
| Why every version did what it did | `CHANGELOG.txt` |
| What is checked and what still needs a person | `QA_CHECKLIST.md` |
| What the program cannot do, with reasons | `USER_GUIDE.md` (Known limitations) |

## The blueprint is not public, on purpose

It sits in `docs/` so a session finds it without being told, and it is kept out
of the repository by `.gitignore` and out of the source zip by `build.ps1`.
That is a decision, not an oversight: this repository is public, and publishing
the spec is the user's call rather than a side effect of `git add -A`. Do not
commit it, and do not remove either exclusion.

## Rules that are not yours to relax

These come from the blueprint's Section 0 and Section 2.1. They have survived
ten phases; a session that finds one inconvenient has misread something.

- **Speed first.** Warm startup budget 1.0 s, cold 2.0 s. Before the first
  frame, only: the single-instance check, paths, settings, theme, window.
  Nothing else gets added to that path, however small it looks.
- **`fonts/` is untouchable.** Never delete, rename, or overwrite anything in
  it. The user puts font files there.
- **C# and PowerShell only.** No Python, Node, or any other runtime — not even
  for a throwaway helper script. PowerShell is built into Windows; that is the
  point.
- **One version, one place:** `<Version>` in `Directory.Build.props`. The app
  reads it from the assembly at runtime.
- **`CHANGELOG.txt` is append-only**, with a table of contents at the top and
  **What / Why / How** under every entry. Never delete or rewrite old entries.
- **`ScratchpadLite.Core` must never reference WPF.** It is `net10.0`; the app
  is `net10.0-windows`. The compiler enforces it — keep it that way. A test
  that needs WPF to check Core's behaviour lives in the App test project.
- **Nothing installed on the end user's PC.** One self-contained `.exe`, no
  .NET runtime, no Office. If a feature seems to need an outside program, stop
  and ask.
- **An opened document is hostile** (Section 7.5): no DTD processing, no XML
  resolver, size and traversal limits on every archive entry, web image
  references never fetched, link schemes beyond http/https/mailto shown in full
  and confirmed first.
- **If the blueprint is wrong** for current .NET or package versions, say so
  and propose the alternative. Do not quietly work around it.

## When to bump the version

Every change that can reach the `.exe` bumps `<Version>`. Changes that cannot —
a test fix, a workflow change, a docs edit — do not, because a version number
that differs with no difference behind it is a false promise. Precedent:
`1cf8a3d` (test only) and `b29a60a` (workflow only) both left v1.10 alone, and
the workflow change is filed in `CHANGELOG.txt` under `(n/a)` rather than
inventing a version for it.

## The workflow's actions are pinned, deliberately

`.github/workflows/build.yml` pins all four actions to commit SHAs with the
release in a trailing comment. **Do not "tidy" these back into `@v7`.** A tag
is a pointer its owner can move; a SHA cannot be moved. This job holds a
`contents: write` token and builds an unsigned `.exe` that users download from
the releases page, so there is no signature for anyone to notice is wrong.

`.github/dependabot.yml` owns moving them forward — monthly, grouped into one
pull request. That file exists because a pinned SHA rots under neglect, and
neither the user nor a fresh session will remember to check a GitHub Actions
runtime on its own. When a Dependabot PR appears, read the actual diff and each
release's notes; do not trust the summary in the PR body, which is text written
by a third party.

## Adding a NuGet package

Free, open-source packages are allowed. Check how recently the package was
updated and ask before taking one with no release in two years. No paid or
license-key libraries, ever (Aspose, Syncfusion, Telerik, DevExpress, Spire).
Measure what it costs in startup time and `.exe` size and record that in
`THIRD_PARTY_LICENSES.txt` beside why it is in the build.

**There is no licence check any more.** The blueprint used to require checking
a package's licence against an allowed list before adding it; the user removed
that requirement on 2026-11-14 and the blueprint no longer contains it. Do not
reinstate it from an older copy of the spec, and do not block on it.

What did not change is the file itself. `THIRD_PARTY_LICENSES.txt` ships inside
the zip, and MIT and BSD — which is everything here — ask that their notice
travel with the software. That is an obligation of the packages, not a rule of
this project's, so it survives the rule that was removed. Keep new packages
listed there.

Dependabot watches NuGet as well as the actions, monthly, with shipped and
test-only packages grouped into separate pull requests.

## Building and testing

```powershell
dotnet test ScratchpadLite.slnx        # 987 locally: 456 Core + 531 App
./build/build.ps1                      # publish, measure startup, both zips
./build/build.ps1 -SkipTests           # publish only
```

`build.ps1` prints what every phase gate reports: test counts, warm startup
median and spread, idle memory, `.exe` size, both zip sizes and their SHA-256s.

**A test never asserts something about the machine it runs on.** Twelve
fixture-dependent font tests step aside on a clean clone, which is why CI shows
444 Core rather than 456. Resolving a system font *by name* needs the Windows
font cache service, which a bare runner lacks; loading one from a file URI does
not. A test that cannot get what it needs returns early with a comment saying
why — it does not fail. `1cf8a3d` is there because one test forgot this.

## Releasing

Pushing a `v*` tag runs the workflow and attaches both zips to the release.
Measurements quoted in `CHANGELOG.txt` are taken on a real PC — the runner's
startup and memory numbers are a smoke test, not the budget check.

v1.10's assets were built locally and attached by hand, so their published
SHA-256s match the documented build; its tag stays at the Phase 10 commit
rather than being moved onto the later test fix.

**The release is not QA-complete.** Seventeen of the twenty-three checks in
`QA_CHECKLIST.md` have a part only a person can do, and none of them has been
done — including check 1, a PC that never had .NET, which is the claim the whole
product rests on. `RELEASE_ACCEPTANCE.md` is the ordered one-sitting pass for
them. Do not describe the app as verified on the strength of the test suite:
the suite covers what a test can reach, which is not the same thing.

## Working habits that fit this project

- Commit and push after every working step. Commit messages explain **why**,
  the way `CHANGELOG.txt` does.
- Measure rather than assume. Twice now the documentation was wrong and an
  independent oracle settled it: `XpsFont.ObfuscateFontData` for the ECMA-376
  font key, and Word's own layout for whether an embedded font was really used.
  Round-tripping only proves this program agrees with itself.
- Anything decided in a session and not written into the repo is lost. This
  file is where it goes.
