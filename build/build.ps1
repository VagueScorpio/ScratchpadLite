# Scratchpad Lite build script (Section 11.2).
#
#   powershell -ExecutionPolicy Bypass -File build\build.ps1
#
# Switches:
#   -SkipTests        publish without running the test projects first
#   -MeasureVariants  also run the three-way publish measurement in Section 11.1
#
# PowerShell is the only scripting language used here, because it is already on
# every Windows PC (Section 0: no other runtimes, even for helpers).

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$MeasureVariants
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$distRoot = Join-Path $repo 'dist'
$distApp = Join-Path $distRoot 'ScratchpadLite'

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------- 1. version --
Step 'Version'
$propsPath = Join-Path $repo 'Directory.Build.props'
$version = ([xml](Get-Content $propsPath)).Project.PropertyGroup.Version
if (-not $version) { throw "No <Version> found in $propsPath" }
Write-Host "Version $version (from Directory.Build.props)"

# ------------------------------------------------------------------ 2. tests --
if ($SkipTests) {
    Write-Warning 'Tests skipped (-SkipTests). A phase gate must never skip them.'
}
else {
    Step 'Tests'
    foreach ($project in @('tests\ScratchpadLite.Core.Tests', 'tests\ScratchpadLite.App.Tests')) {
        Write-Host "dotnet test $project"
        dotnet test (Join-Path $repo $project) -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Tests failed in $project" }
    }
}

# ---------------------------------------------------------------- 3. publish --
Step 'Publish'
if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
dotnet publish (Join-Path $repo 'src\ScratchpadLite.App') -c Release -r win-x64 -o $distApp --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

# ------------------------------------------------------------------ 4. fonts --
# The bundle is copied in, and nothing in the repo fonts folder is ever changed.
Step 'Font bundle'
$distFonts = Join-Path $distApp 'fonts'
New-Item -ItemType Directory -Path $distFonts -Force | Out-Null
Copy-Item (Join-Path $repo 'src\ScratchpadLite.App\Resources\font-folder-README.txt') `
    (Join-Path $distFonts 'README.txt') -Force

$repoFonts = Join-Path $repo 'fonts'
$bundle = @(Get-ChildItem $repoFonts -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'README.txt' })
if ($bundle.Count -eq 0) {
    Write-Host 'The fonts folder holds no bundle yet; shipping the README only.'
}
else {
    foreach ($f in $bundle) {
        $relative = $f.FullName.Substring($repoFonts.Length).TrimStart('\')
        $target = Join-Path $distFonts $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item $f.FullName $target -Force
    }
    $bundleMb = [math]::Round(($bundle | Measure-Object -Sum Length).Sum / 1MB, 1)
    Write-Host "Copied $($bundle.Count) font files ($bundleMb MB)"
}

# --------------------------------------------------------- 5. font licenses --
Step 'FONT_LICENSES.txt'
# Section 6.6.6: regenerated from the bundle through FontInspector, flagging any
# font with no license text and any whose license forbids redistribution.
#
# The published .exe generates it, rather than this script reading font tables
# itself: the reader already exists in Core, and a second one in PowerShell
# would be a second thing to keep right. --font-licenses writes the file and
# exits without showing a window.
$licenses = Join-Path $repo 'FONT_LICENSES.txt'
$publishedExe = Join-Path $distApp 'ScratchpadLite.exe'

if (Test-Path $publishedExe) {
    Start-Process -FilePath $publishedExe `
        -ArgumentList '--font-licenses', "`"$licenses`"" `
        -Wait -NoNewWindow

    if (Test-Path $licenses) {
        $flagged = Select-String -Path $licenses -Pattern 'NEEDS A DECISION' -Quiet
        if ($flagged) {
            Write-Warning 'Some bundled fonts are flagged. See the top of FONT_LICENSES.txt.'
        } else {
            Write-Host 'Regenerated, nothing flagged.'
        }
    } else {
        Write-Warning 'FONT_LICENSES.txt was not written; see data\logs.'
    }
} else {
    Write-Warning 'No published .exe yet, so FONT_LICENSES.txt was not regenerated.'
}

# ------------------------------------------------------------------- 6. docs --
Step 'Documents'
$docs = @{
    'README.md'                 = 'README.txt'
    'USER_GUIDE.md'             = 'USER_GUIDE.md'
    'CHANGELOG.txt'             = 'CHANGELOG.txt'
    'THIRD_PARTY_LICENSES.txt'  = 'THIRD_PARTY_LICENSES.txt'
    'FONT_LICENSES.txt'         = 'FONT_LICENSES.txt'
}
foreach ($source in $docs.Keys) {
    $from = Join-Path $repo $source
    if (Test-Path $from) { Copy-Item $from (Join-Path $distApp $docs[$source]) -Force }
    else { Write-Warning "missing: $source" }
}

# ---------------------------------------------------------- 7. startup check --
Step 'Startup check'
$exe = Join-Path $distApp 'ScratchpadLite.exe'

function Measure-Startup($exePath, $runs) {
    # Two things to get right here, both learned the hard way in Phase 0.
    #
    # The app is a GUI executable, so `& $exe` does not wait for it: without
    # -Wait, five launches run at once and the numbers are meaningless.
    #
    # And a GUI executable has no stdout to pipe. The app writes the result to
    # data\startup-measure.txt as well as to the console, and that file is what
    # is read here.
    $measureFile = Join-Path (Split-Path $exePath -Parent) 'data\startup-measure.txt'
    $values = @()

    for ($i = 1; $i -le $runs; $i++) {
        if (Test-Path $measureFile) { Remove-Item $measureFile -Force }
        Start-Process -FilePath $exePath -ArgumentList '--measure-startup' -Wait -NoNewWindow

        if ((Test-Path $measureFile) -and ((Get-Content $measureFile -Raw) -match 'startup_ms=([\d\.]+)')) {
            $ms = [double]$Matches[1]
            $values += $ms
            $label = if ($i -eq 1) { ' (first run: also prepares the fonts)' } else { '' }
            Write-Host ("  run {0}: {1:F0} ms{2}" -f $i, $ms, $label)
        }
        else {
            Write-Warning "run $i produced no measurement"
        }
    }

    return $values
}

$runs = Measure-Startup $exe 5
if ($runs.Count -ge 2) {
    # Runs 2 to 5 only: the first run also prepares the fonts (Section 11.2).
    $warm = $runs[1..($runs.Count - 1)] | Sort-Object
    $median = $warm[[int]([math]::Floor($warm.Count / 2))]
    Write-Host ("Median of runs 2-{0}: {1:F0} ms" -f $runs.Count, $median)
    if ($median -gt 1000) {
        Write-Warning "Startup is over the 1.0 s warm budget (Section 2.1). Report what caused it before continuing."
    }
}

# --------------------------------------- 7b. three-way publish measurement ----
if ($MeasureVariants) {
    Step 'Publish variants (Section 11.1)'
    $variants = @(
        @{ Name = 'compression off, native libraries inside the exe'
           Args = @('-p:EnableCompressionInSingleFile=false', '-p:IncludeNativeLibrariesForSelfExtract=true') }
        @{ Name = 'compression off, native libraries beside the exe'
           Args = @('-p:EnableCompressionInSingleFile=false', '-p:IncludeNativeLibrariesForSelfExtract=false') }
        @{ Name = 'compression on (comparison only)'
           Args = @('-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }
    )

    foreach ($v in $variants) {
        $out = Join-Path $distRoot ('variant-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
        dotnet publish (Join-Path $repo 'src\ScratchpadLite.App') -c Release -r win-x64 -o $out --nologo @($v.Args) | Out-Null
        $variantExe = Join-Path $out 'ScratchpadLite.exe'
        $sizeMb = [math]::Round((Get-Item $variantExe).Length / 1MB, 1)
        Write-Host "`n$($v.Name): exe $sizeMb MB"
        $vr = Measure-Startup $variantExe 5
        if ($vr.Count -ge 2) {
            $vw = $vr[1..($vr.Count - 1)] | Sort-Object
            Write-Host ("  median: {0:F0} ms" -f $vw[[int]([math]::Floor($vw.Count / 2))])
        }
    }
    Write-Host "`nUse the fastest. If that is the variant with native libraries beside the exe, it breaks one-file: ask before choosing it."
}

# -------------------------------------------------------------------- 8. zip --
Step 'Zip'
# The data folder is created by the startup check above, and must not ship.
$distData = Join-Path $distApp 'data'
if (Test-Path $distData) { Remove-Item $distData -Recurse -Force }
Get-ChildItem $distRoot -Directory | Where-Object { $_.Name -like 'variant-*' } | Remove-Item -Recurse -Force

$zip = Join-Path $distRoot "ScratchpadLite-v$version-windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $distApp -DestinationPath $zip

# ----------------------------------------------------------------- 9. report --
Step 'Report'
$exeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
$fontMb = if ($bundle.Count -gt 0) { [math]::Round(($bundle | Measure-Object -Sum Length).Sum / 1MB, 1) } else { 0 }

Write-Host "exe:         $exeMb MB"
Write-Host "font bundle: $fontMb MB"
Write-Host "zip:         $zipMb MB"
Write-Host "zip:         $zip"
Write-Host "sha256:      $((Get-FileHash $zip -Algorithm SHA256).Hash)"
