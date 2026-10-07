#requires -Version 5.1
<#
.SYNOPSIS
    Tests for distribution\lib\VortexUi.ps1 and a syntax check of the installer scripts.

.DESCRIPTION
    Self-contained, like ClaudeConfig.Tests.ps1 (no Pester). Run it under Windows
    PowerShell 5.1, the engine install.bat and the in-app updater use:

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File distribution\tests\VortexUi.Tests.ps1

    It also runs under pwsh. Exit code 0 = all pass, 1 = at least one failure.

    The scripts are only parsed and the screens only drawn: nothing is installed.
#>

$ErrorActionPreference = 'Stop'
$distribution = Split-Path -Parent $PSScriptRoot
. (Join-Path $distribution 'lib\VortexUi.ps1')

$script:Failures = 0
$script:Passes = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if ($Condition) { $script:Passes++ }
    else { $script:Failures++; Write-Host "FAIL: $Message" -ForegroundColor Red }
}

# ── The scripts parse (under 5.1 this catches syntax only newer engines accept) ──

foreach ($name in @('install.ps1', 'uninstall.ps1', 'lib\VortexUi.ps1', 'tests\VortexUi.Demo.ps1')) {
    $tokens = $null; $errors = $null
    [void] [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $distribution $name), [ref] $tokens, [ref] $errors)
    $first = if ($errors.Count -gt 0) { "line $($errors[0].Extent.StartLineNumber): $($errors[0].Message)" } else { '' }
    Assert-True ($errors.Count -eq 0) "$name does not parse. $first"
}

# ── Texts ────────────────────────────────────────────────────────────────

$english = $script:VxText['en']
foreach ($lang in @('es', 'it')) {
    $table = $script:VxText[$lang]
    foreach ($key in $english.Keys) {
        Assert-True ($table.ContainsKey($key)) "[$lang] has no text for '$key'"
        if (-not $table.ContainsKey($key)) { continue }
        $wanted = ([regex]::Matches($english[$key], '\{\d') | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ','
        $found = ([regex]::Matches($table[$key], '\{\d') | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ','
        Assert-True ($wanted -eq $found) "[$lang] '$key' has placeholders '$found', English has '$wanted'"
    }
    foreach ($key in $table.Keys) {
        Assert-True ($english.ContainsKey($key)) "[$lang] has '$key', which English does not"
    }
}

$env:VORTEX_UI = 'plain'
Initialize-VxUi -Language 'es-AR'
Assert-True ($script:Vx.Lang -eq 'es') 'es-AR picks Spanish'
# A file saved without its byte order mark turns accents into two characters.
$versionText = Get-VxText 'SubInstall' @('1.2.0')
Assert-True ($versionText -eq ('Instalando la versi' + [char] 0xF3 + 'n 1.2.0')) "Spanish accents are intact (got '$versionText')"
Assert-True ((Get-VxText 'NoSuchKey') -eq 'NoSuchKey') 'an unknown key comes back as itself'

Initialize-VxUi -Language 'pt-BR'
Assert-True ($script:Vx.Lang -eq 'en') 'a language without texts falls back to English'

foreach ($yes in @('y', 'Y', 'yes', 's', 'S', 'si', ('s' + [char] 0xED), ('s' + [char] 0xEC), ' y ')) {
    Assert-True (Test-VxYes $yes) "'$yes' means yes"
}
foreach ($no in @('', 'n', 'no', 'q', 'yy', '1')) {
    Assert-True (-not (Test-VxYes $no)) "'$no' does not mean yes"
}

# ── The vortex ───────────────────────────────────────────────────────────

$frame = Get-VxVortexFrame -Phase 0.0
Assert-True ($frame.Count -eq 17) "the vortex is 17 lines tall (got $($frame.Count))"
$widest = ($frame | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
Assert-True ($widest -le 6 + 39) "no line is wider than the indent plus 39 (got $widest)"
$ink = ($frame -join '') -replace ' ', ''
Assert-True ($ink.Length -gt 150) "the vortex has something in it (got $($ink.Length) characters)"
Assert-True (($ink -replace '[.:\-=+*#%@]', '').Length -eq 0) 'the plain vortex only uses the ramp characters'

$turned = Get-VxVortexFrame -Phase (2 * [Math]::PI)
Assert-True (($frame -join "`n") -eq ($turned -join "`n")) 'a full phase turn brings the same picture back'
$moved = Get-VxVortexFrame -Phase 1.0
Assert-True (($frame -join "`n") -ne ($moved -join "`n")) 'another phase is another picture'
$faint = (Get-VxVortexFrame -Phase 0.0 -Intensity 0.0) -join ''
Assert-True ($faint.Trim().Length -eq 0) 'intensity 0 draws nothing'

$colored = Get-VxVortexFrame -Phase 0.0 -Color
Assert-True (($colored -join '').Contains([string] [char] 27 + '[38;2;')) 'the colored vortex carries 24-bit color codes'

# ── Progress bar and paths ───────────────────────────────────────────────

$bar = Format-VxBar 0 100 'x'
Assert-True ($bar[1].Length -eq $script:VxItemIndent.Length) 'an empty bar has no filled cell'
Assert-True ($bar[3].Length -eq 28) 'an empty bar has 28 empty cells'
$bar = Format-VxBar 100 100 'x'
Assert-True ($bar[1].Length -eq $script:VxItemIndent.Length + 28) 'a full bar has 28 filled cells'
Assert-True ($bar[5].Trim() -eq '100 %') "a full bar says 100 % (got '$($bar[5])')"
$bar = Format-VxBar 5 0 'x'
Assert-True ($bar[5].Trim() -eq '100 %') 'nothing to copy counts as done'

if ($env:USERPROFILE -and $env:APPDATA) {
    Assert-True ((Format-VxPath (Join-Path $env:APPDATA 'Claude\x.json')) -eq '%APPDATA%\Claude\x.json') 'a path under AppData\Roaming is shown with %APPDATA%'
    Assert-True ((Format-VxPath (Join-Path $env:USERPROFILE '.revitcortex\server')) -eq '%USERPROFILE%\.revitcortex\server') 'a path under the profile is shown with %USERPROFILE%'
}
Assert-True ((Format-VxPath 'C:\ProgramData\Autodesk') -eq 'C:\ProgramData\Autodesk') 'any other path is left alone'

# ── Long sentences continue under themselves ─────────────────────────────

$script:Vx.Width = 60
$wrapped = Split-VxText ('word ' * 30).Trim() 9
Assert-True ($wrapped.Count -gt 1) 'a long sentence is split into several lines'
Assert-True ((($wrapped | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum) -le 50) 'no line is longer than the window minus the indent'
Assert-True (($wrapped -join ' ') -eq ('word ' * 30).Trim()) 'splitting loses no word'
$whole = Split-VxText 'C:\a\very\long\path\that\has\no\space\in\it\at\all\and\keeps\going\on\and\on' 9
Assert-True ($whole.Count -eq 1) 'a path longer than a line is left whole'
Assert-True ((Split-VxText '' 9).Count -eq 1) 'an empty text is one empty line'
$script:Vx.Width = 80

# ── Every screen draws, in the two modes that need no real console ───────

$demo = Join-Path $PSScriptRoot 'VortexUi.Demo.ps1'
foreach ($mode in @('plain', 'ansi')) {
    foreach ($scenario in @('install', 'update', 'failure', 'uninstall')) {
        foreach ($lang in @('en', 'es', 'it')) {
            $env:VORTEX_UI = $mode
            try {
                & $demo -Scenario $scenario -Language $lang -Fast 6>$null
                $script:Passes++
            } catch {
                $script:Failures++
                Write-Host "FAIL: $scenario / $lang / $mode threw: $_" -ForegroundColor Red
            }
        }
    }
}
$env:VORTEX_UI = $null

Write-Host ""
Write-Host "VortexUi tests: $script:Passes passed, $script:Failures failed"
if ($script:Failures -gt 0) { exit 1 }
exit 0
