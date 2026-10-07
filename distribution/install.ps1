#requires -Version 5.1
param(
    # When set, skips all interactive prompts and uses safe defaults:
    #   - Defender exclusion: not added
    #   - AI client: Claude Desktop (option 1)
    #   - the window closes by itself a few seconds after the result
    # The in-app updater (UpdateChecker.LaunchInstaller) always passes this flag.
    [switch] $Silent
)
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

. (Join-Path $ScriptDir 'lib\VortexUi.ps1')
. (Join-Path $ScriptDir 'lib\ClaudeConfig.ps1')
. (Join-Path $ScriptDir 'lib\RevitDeploy.ps1')
. (Join-Path $ScriptDir 'lib\GitInstall.ps1')

Initialize-VxUi

function T {
    # A text of the installer, in its language: T 'Files' 344
    param([string] $Key)
    Get-VxText $Key $args
}

function Stop-Install {
    # Shows why the install stopped, leaves it on screen, and exits with 1.
    param([string] $Message)
    Show-VxSummary -Title (T 'FailedTitle') -Notes @($Message) -Failed
    # During an update nobody is there to press a key, and the window would
    # vanish with the error in it: leave it up for half a minute.
    if ($Silent) { Wait-VxClose -Seconds 30 } else { Wait-VxClose }
    exit 1
}

# --- Self-elevate (the machine-scope Revit add-in folder needs admin; the user-scope
#     fallback does not, but we try machine first for parity with the Inno installer) ---
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Vx
    Write-Vx 'soft', "  $(T 'Elevating')"
    $silentArg = if ($Silent) { ' -Silent' } else { '' }
    try {
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`"$silentArg"
    } catch {
        # The Windows prompt was declined.
        Write-Vx 'fail', "  $(T 'ElevationDenied')"
        exit 1
    }
    exit
}

# The version in this package, for the title (any of the plugin builds has it).
$packageVersion = $null
try {
    $pluginDll = Get-ChildItem (Join-Path $ScriptDir 'plugin') -Filter 'RevitCortex.Plugin.dll' -Recurse -ErrorAction Stop | Select-Object -First 1
    if ($pluginDll) {
        $packageVersion = "$($pluginDll.VersionInfo.FileVersion)" -replace '\.0$', ''
    }
} catch { }

$subtitle = if ($Silent) {
    if ($packageVersion) { T 'SubUpdate' $packageVersion } else { T 'SubUpdatePlain' }
} else {
    if ($packageVersion) { T 'SubInstall' $packageVersion } else { T 'SubInstallPlain' }
}

try { $Host.UI.RawUI.WindowTitle = 'RVT Vortex' } catch { }
try { Clear-Host } catch { }

try {
    $steps = 6

    # --- Step 1: Revit must be closed (it keeps the plugin DLLs locked) ---
    if ($Silent) {
        # The updater starts us while Revit is still open and closes it a moment
        # later: the vortex turns until the process is gone.
        $revitGone = Show-VxBanner -Subtitle $subtitle -While { Test-RevitRunning } -WhileText (T 'WaitRevit') -TimeoutSeconds 120
        Write-VxStep 1 $steps (T 'StepChecks')
        if (-not $revitGone) { Stop-Install (T 'RevitStillOpen' 120) }
    } else {
        [void] (Show-VxBanner -Subtitle $subtitle)
        Write-VxStep 1 $steps (T 'StepChecks')
        while (Test-RevitRunning) {
            Write-VxWarn (T 'RevitOpen')
            $answer = Read-VxLine (T 'RevitOpenPrompt')
            if ("$answer".Trim() -match '^q$') { Stop-Install (T 'Aborted') }
        }
    }
    Write-VxOk (T 'RevitClosed')

    # --- Step 2: Detect Revit versions ---
    Write-VxStep 2 $steps (T 'StepDetect')

    $configMap = [ordered]@{ "2023" = "R23"; "2024" = "R24"; "2025" = "R25"; "2026" = "R26"; "2027" = "R27" }
    $machineAddinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
    $foundVersions   = @()   # installed + plugin available in this ZIP
    $skippedVersions = @()   # installed but plugin not in this ZIP

    foreach ($ver in $configMap.Keys) {
        # 1. Detect whether this Revit version is actually installed on the machine.
        #    Check three signals in order: Addins folder, registry, Revit.exe.
        $machineVerDir = Join-Path $machineAddinsRoot $ver
        $revitInstalled = Test-Path $machineVerDir

        if (-not $revitInstalled) {
            foreach ($rp in @(
                "HKLM:\SOFTWARE\Autodesk\Revit\$ver",
                "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit\$ver",
                "HKCU:\SOFTWARE\Autodesk\Revit\$ver"
            )) { if (Test-Path $rp) { $revitInstalled = $true; break } }
        }

        if (-not $revitInstalled) {
            $revitExe = "C:\Program Files\Autodesk\Revit $ver\Revit.exe"
            if (Test-Path $revitExe) { $revitInstalled = $true }
        }

        if (-not $revitInstalled) { continue }

        # 2. Check whether this ZIP contains the plugin build for this version.
        $pluginDir = Join-Path $ScriptDir "plugin\$($configMap[$ver])"
        if (-not (Test-Path $pluginDir)) {
            $skippedVersions += $ver   # Revit installed but not bundled in this package
            continue
        }

        $foundVersions += $ver
    }

    if ($foundVersions.Count -eq 0 -and $skippedVersions.Count -eq 0) { Stop-Install (T 'NoRevit') }
    if ($foundVersions.Count -eq 0) { Stop-Install (T 'NotBundled' ($skippedVersions -join ', ')) }

    Write-VxOk ("Revit {0}" -f ($foundVersions -join ', '))
    if ($skippedVersions.Count -gt 0) { Write-VxWarn (T 'SkippedRevit' ($skippedVersions -join ', ')) }

    # Port 8080 warning (non-fatal)
    try {
        if (Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue) {
            Write-VxWarn (T 'PortBusy')
        }
    } catch {}

    # --- Step 3: Install the plugin (machine -> user scope fallback on ACL errors) ---
    Write-VxStep 3 $steps (T 'StepPlugin')

    $addinTemplate = Join-Path $ScriptDir "RevitCortex.addin"
    if (-not (Test-Path $addinTemplate)) { Stop-Install (T 'AddinMissing' $addinTemplate) }

    $deployFailures = @()
    foreach ($ver in $foundVersions) {
        $suffix = $configMap[$ver]
        $sourceDir = Join-Path $ScriptDir "plugin\$suffix"
        $r = Copy-RevitAddin -Version $ver -PluginSource $sourceDir -AddinManifest $addinTemplate
        if ($r.Ok) {
            $where = Format-VxPath $r.TargetDir
            if ($r.Scope -eq 'user') { $where = "$where ($(T 'UserScope'))" }
            Write-VxOk "Revit $ver" $where
        } else {
            Write-VxFail "Revit ${ver}: $($r.Error)"
            $deployFailures += $ver
        }
    }

    if ($deployFailures.Count -gt 0) { Stop-Install (T 'PluginFailed' ($deployFailures -join ', ')) }

    # --- Step 4: Install the MCP server (C#, self-contained) ---
    Write-VxStep 4 $steps (T 'StepServer')

    $serverSource = Join-Path $ScriptDir "server"
    $serverTarget = Join-Path $env:USERPROFILE ".revitcortex\server"

    if (-not (Test-Path $serverSource)) { Stop-Install (T 'ServerMissing' $serverSource) }

    # The MCP client (Claude Desktop, Claude Code, Cursor...) keeps RevitCortex.Server.exe
    # running while it is open, which locks clrjit.dll and the other server files.
    # Stop it first; the client restarts it on its next launch.
    $running = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
    if ($running) {
        Write-VxWarn (T 'StoppingServer')
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        try { $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue } catch {}
    }

    if (Test-Path $serverTarget) {
        $removed = $false
        for ($attempt = 1; $attempt -le 5 -and -not $removed; $attempt++) {
            try {
                Remove-Item $serverTarget -Recurse -Force -ErrorAction Stop
                $removed = $true
            } catch {
                if ($attempt -eq 5) { Stop-Install (T 'ServerLocked' (Format-VxPath $serverTarget)) }
                Start-Sleep -Seconds 2
            }
        }
    }
    New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null

    # File by file, so the bar shows how far the copy really is. Same result as
    # Copy-Item "$serverSource\*" $serverTarget -Recurse -Force.
    $sourceRoot = (Resolve-Path $serverSource).Path.TrimEnd('\', '/')
    $serverFiles = @(Get-ChildItem $sourceRoot -Recurse -File -Force)
    $filesText = T 'Files' $serverFiles.Count
    try {
        $lastDraw = [System.Diagnostics.Stopwatch]::StartNew()
        $copied = 0
        foreach ($file in $serverFiles) {
            $relative = $file.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')
            $destination = Join-Path $serverTarget $relative
            $destinationDir = Split-Path -Parent $destination
            if (-not (Test-Path -LiteralPath $destinationDir)) {
                New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
            }
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force -ErrorAction Stop
            $copied++
            if ($lastDraw.ElapsedMilliseconds -ge 40) {
                Write-VxProgress $copied $serverFiles.Count $filesText
                $lastDraw.Restart()
            }
        }
        # Folders with nothing in them are part of the tree too.
        foreach ($dir in @(Get-ChildItem $sourceRoot -Recurse -Directory -Force)) {
            $destinationDir = Join-Path $serverTarget ($dir.FullName.Substring($sourceRoot.Length).TrimStart('\', '/'))
            if (-not (Test-Path -LiteralPath $destinationDir)) {
                New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
            }
        }
        Complete-VxProgress $serverFiles.Count $filesText
    } catch {
        Write-Vx
        Stop-Install (T 'CopyFailed' "$_")
    }

    $serverExe = Join-Path $serverTarget "RevitCortex.Server.exe"
    if (-not (Test-Path $serverExe)) { Stop-Install (T 'ServerExeMissing') }

    # Unblock Zone.Identifier so Defender/SmartScreen don't block on first run
    Get-ChildItem $serverTarget -Recurse -File | ForEach-Object { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue }

    Write-VxOk (T 'ServerInstalled') (Format-VxPath $serverExe)

    # Defender exclusion (opt-in, idempotent), never during an update
    if (-not $Silent -and (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) {
        $existing = @()
        try { $mp = Get-MpPreference -ErrorAction SilentlyContinue; if ($mp -and $mp.ExclusionPath) { $existing = @($mp.ExclusionPath) } } catch {}
        if ($existing -notcontains $serverTarget) {
            if (Test-VxYes (Read-VxLine (T 'DefenderPrompt'))) {
                Add-MpPreference -ExclusionPath $serverTarget -ErrorAction SilentlyContinue
                Write-VxOk (T 'DefenderAdded')
            }
        }
    }

    # AI skill: install the RevitCortex skill to user-level paths.
    # Guard on the client root (.claude / .codex) existing: if the user doesn't have
    # the client at all we skip (no profile pollution). If the client root exists
    # but skills/ doesn't yet, we create it: a first-time skill install must work.
    $skillSrc = Join-Path $ScriptDir "ai-skills\revitcortex"
    if (Test-Path $skillSrc) {
        $skillTargets = @(
            @{ ClientRoot = (Join-Path $env:USERPROFILE ".claude");  Target = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex");  Name = "Claude Code" },
            @{ ClientRoot = (Join-Path $env:USERPROFILE ".codex");   Target = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex");   Name = "Codex CLI" }
        )
        foreach ($entry in $skillTargets) {
            if (Test-Path $entry.ClientRoot) {
                if (-not (Test-Path $entry.Target)) { New-Item -ItemType Directory -Path $entry.Target -Force | Out-Null }
                Copy-Item "$skillSrc\*" $entry.Target -Recurse -Force
                Write-VxOk (T 'SkillInstalled' $entry.Name)
            }
        }
    }

    # --- Step 5: Git (Claude Code needs it for many workflows) ---
    Write-VxStep 5 $steps (T 'StepGit')
    $gitOk = Test-GitInstalled
    if ($gitOk) {
        Write-VxOk (T 'GitPresent')
    } else {
        Write-VxNote (T 'GitInstalling')
        $gitOk = [bool] (Ensure-Git -Quiet 6>$null)
        # winget draws on the console itself; make sure colors still work after it.
        Initialize-VxUi
        if ($gitOk) { Write-VxOk (T 'GitInstalled') } else { Write-VxWarn (T 'GitFailed') }
    }

    # --- Step 6: Connect the AI client ---
    Write-VxStep 6 $steps (T 'StepClient')

    if ($Silent) {
        # An update asks nothing: Claude Desktop, the safe default.
        $choice = "1"
    } else {
        Write-Vx 'soft', "$($script:VxItemIndent)$(T 'ClientQuestion')"
        Write-Vx 'accent', "$($script:VxItemIndent)  1  ", 'soft', 'Claude Desktop'
        Write-Vx 'accent', "$($script:VxItemIndent)  2  ", 'soft', 'Claude Code (CLI)'
        Write-Vx 'accent', "$($script:VxItemIndent)  3  ", 'soft', (T 'ClientBoth')
        Write-Vx 'accent', "$($script:VxItemIndent)  4  ", 'soft', (T 'ClientSkip')
        $choice = "$(Read-VxLine (T 'ClientPrompt'))".Trim()
    }

    $clients = @()

    if ($choice -eq "1" -or $choice -eq "3") {
        $configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
        try {
            $result = Merge-ClaudeMcpServer -ConfigPath $configPath -ServerName 'revitcortex' -Command $serverExe -Arguments @()
            Write-VxOk (T 'DesktopDone')
            if ($result.BackupPath) { Write-VxNote (T 'DesktopBackup' (Format-VxPath $result.BackupPath)) }
            $clients += 'Claude Desktop'
        } catch {
            Write-VxFail (T 'DesktopFailed' "$_")
            Write-VxNote (T 'DesktopUntouched')
        }
    }

    if ($choice -eq "2" -or $choice -eq "3") {
        $claudeCli = Get-Command claude -ErrorAction SilentlyContinue
        if ($claudeCli) {
            try {
                & claude mcp add revitcortex $serverExe 2>$null | Out-Null
                Write-VxOk (T 'CodeDone')
                $clients += 'Claude Code'
            } catch {
                Write-VxWarn (T 'CodeFailed' "$_")
            }
        } else {
            Write-VxWarn (T 'CodeMissing')
            # A command to copy: on one line, never split to fit the window.
            Write-Vx 'soft', "$($script:VxItemIndent)  claude mcp add revitcortex `"$serverExe`""
        }
    }

    if ($choice -ne "1" -and $choice -ne "2" -and $choice -ne "3") {
        Write-VxNote (T 'ClientLater')
        Write-VxNote (T 'ClientLaterDesktop')
        Write-VxNote 'Claude Code:'
        # A command to copy: on one line, never split to fit the window.
        Write-Vx 'soft', "$($script:VxItemIndent)  claude mcp add revitcortex `"$serverExe`""
    }

    # --- Summary ---
    $gitText = if ($gitOk) { T 'GitOk' } else { T 'GitMissing' }
    $rows = @(
        @((T 'RowPlugin'), ("Revit {0}" -f ($foundVersions -join ', '))),
        @((T 'RowServer'), (Format-VxPath $serverExe)),
        @((T 'RowGit'), $gitText)
    )
    if ($clients.Count -gt 0) { $rows += , @((T 'RowClient'), ($clients -join ', ')) }

    $title = if ($Silent) { T 'SummaryUpdated' } else { T 'SummaryInstalled' }
    Show-VxSummary -Title $title -Rows $rows -NextTitle (T 'NextTitle') -Next @((T 'NextRevit'), (T 'NextClient'))

    # During an update nobody is there to press Enter: show the result, then close.
    if ($Silent) { Wait-VxClose -Seconds 8 } else { Wait-VxClose }
} catch {
    # Anything not handled above: say what it was instead of letting the
    # window close on a raw PowerShell error.
    Stop-Install "$($_.Exception.Message)"
}
