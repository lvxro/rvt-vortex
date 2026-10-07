#requires -Version 5.1
<#
.SYNOPSIS
    Plays the installer's screens with made-up data. Nothing is installed.

.DESCRIPTION
    A way to look at a change to distribution\lib\VortexUi.ps1 without running
    an install:

        powershell -NoProfile -ExecutionPolicy Bypass -File distribution\tests\VortexUi.Demo.ps1
        powershell ... -File distribution\tests\VortexUi.Demo.ps1 -Scenario update -Language es

    Set VORTEX_UI=ansi (colors, nothing redrawn) or VORTEX_UI=plain to see the
    two fallbacks; the UI preview workflow captures the ansi one as text.

    The steps here imitate install.ps1 and uninstall.ps1; when those change
    what they say, change it here too.
#>
param(
    [ValidateSet('install', 'update', 'failure', 'uninstall')]
    [string] $Scenario = 'install',

    # en, es or it. Default: the Windows display language.
    [string] $Language,

    # No pauses: for capturing the screens.
    [switch] $Fast
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\VortexUi.ps1')
Initialize-VxUi -Language $Language

function T { param([string] $Key) Get-VxText $Key $args }
function Pause-Demo { param([int] $Milliseconds) if (-not $Fast) { Start-Sleep -Milliseconds $Milliseconds } }

$version = '1.2.0'
$server = '%USERPROFILE%\.revitcortex\server\RevitCortex.Server.exe'

function Show-FakeCopy {
    # The progress bar, over a copy that takes about a second and a half.
    param([int] $Files)
    $text = T 'Files' $Files
    for ($i = 1; $i -le $Files; $i += 7) {
        Write-VxProgress $i $Files $text
        Pause-Demo 28
    }
    Complete-VxProgress $Files $text
}

if ($Scenario -eq 'uninstall') {
    [void] (Show-VxBanner -Subtitle (T 'SubUninstall'))
    Write-Vx
    Write-Vx 'glow', "$($script:VxItemIndent)$(T 'UninstallConfirm') ", 'ink', 's'
    Write-VxStep 1 3 (T 'StepRemovePlugin')
    Write-VxOk 'Revit 2025' 'C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex'
    Write-VxOk 'Revit 2027' '%APPDATA%\Autodesk\Revit\Addins\2027\RevitCortex'
    Pause-Demo 300
    Write-VxStep 2 3 (T 'StepRemoveServer')
    Write-VxOk (T 'ServerRemoved') '%USERPROFILE%\.revitcortex\server'
    Pause-Demo 300
    Write-VxStep 3 3 (T 'StepRemoveClient')
    Write-VxOk (T 'DesktopRemoved')
    Show-VxSummary -Title (T 'SummaryRemoved') -Notes @((T 'DataKept' '%USERPROFILE%\.revitcortex')) `
        -NextTitle (T 'NextTitle') -Next @((T 'NextAfterRemove'))
    return
}

$updating = $Scenario -eq 'update'
$steps = 6

if ($updating) {
    # Revit takes a few seconds to close after the update starts.
    $closing = [System.Diagnostics.Stopwatch]::StartNew()
    $wait = 3200; if ($Fast) { $wait = 0 }
    [void] (Show-VxBanner -Subtitle (T 'SubUpdate' $version) -WhileText (T 'WaitRevit') `
        -While { $closing.ElapsedMilliseconds -lt $wait })
} else {
    [void] (Show-VxBanner -Subtitle (T 'SubInstall' $version))
}

Write-VxStep 1 $steps (T 'StepChecks')
Write-VxOk (T 'RevitClosed')
Pause-Demo 250

Write-VxStep 2 $steps (T 'StepDetect')
Pause-Demo 350
Write-VxOk 'Revit 2025, 2027'
if ($Scenario -eq 'install') { Write-VxWarn (T 'SkippedRevit' '2022') }

Write-VxStep 3 $steps (T 'StepPlugin')
Pause-Demo 400
Write-VxOk 'Revit 2025' 'C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex'
Pause-Demo 300
Write-VxOk 'Revit 2027' "%APPDATA%\Autodesk\Revit\Addins\2027\RevitCortex ($(T 'UserScope'))"

Write-VxStep 4 $steps (T 'StepServer')
if ($Scenario -eq 'failure') {
    Write-VxWarn (T 'StoppingServer')
    Pause-Demo 900
    Show-VxSummary -Title (T 'FailedTitle') -Notes @((T 'ServerLocked' '%USERPROFILE%\.revitcortex\server')) -Failed
    return
}
Show-FakeCopy 344
Write-VxOk (T 'ServerInstalled') $server
Write-VxOk (T 'SkillInstalled' 'Claude Code')

Write-VxStep 5 $steps (T 'StepGit')
Pause-Demo 200
Write-VxOk (T 'GitPresent')

Write-VxStep 6 $steps (T 'StepClient')
if (-not $updating) {
    Write-Vx 'soft', "$($script:VxItemIndent)$(T 'ClientQuestion')"
    Write-Vx 'accent', "$($script:VxItemIndent)  1  ", 'soft', 'Claude Desktop'
    Write-Vx 'accent', "$($script:VxItemIndent)  2  ", 'soft', 'Claude Code (CLI)'
    Write-Vx 'accent', "$($script:VxItemIndent)  3  ", 'soft', (T 'ClientBoth')
    Write-Vx 'accent', "$($script:VxItemIndent)  4  ", 'soft', (T 'ClientSkip')
    Pause-Demo 700
    Write-Vx 'glow', "$($script:VxItemIndent)$(T 'ClientPrompt') ", 'ink', '1'
}
Pause-Demo 250
Write-VxOk (T 'DesktopDone')

$title = if ($updating) { T 'SummaryUpdated' } else { T 'SummaryInstalled' }
Show-VxSummary -Title $title -NextTitle (T 'NextTitle') -Next @((T 'NextRevit'), (T 'NextClient')) -Rows @(
    @((T 'RowPlugin'), 'Revit 2025, 2027'),
    @((T 'RowServer'), $server),
    @((T 'RowGit'), (T 'GitOk')),
    @((T 'RowClient'), 'Claude Desktop'))

if ($updating -and -not $Fast) { Wait-VxClose -Seconds 3 }
