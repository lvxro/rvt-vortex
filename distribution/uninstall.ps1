#requires -Version 5.1
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

. (Join-Path $ScriptDir 'lib\VortexUi.ps1')
. (Join-Path $ScriptDir 'lib\ClaudeConfig.ps1')
. (Join-Path $ScriptDir 'lib\RevitDeploy.ps1')

Initialize-VxUi

function T {
    # A text of the installer, in its language.
    param([string] $Key)
    Get-VxText $Key $args
}

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Vx
    Write-Vx 'soft', "  $(T 'Elevating')"
    try {
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    } catch {
        Write-Vx 'fail', "  $(T 'ElevationDenied')"
        exit 1
    }
    exit
}

try { $Host.UI.RawUI.WindowTitle = 'RVT Vortex' } catch { }
try { Clear-Host } catch { }

try {
    [void] (Show-VxBanner -Subtitle (T 'SubUninstall'))
    Write-Vx

    if (-not (Test-VxYes (Read-VxLine (T 'UninstallConfirm')))) {
        Write-VxNote (T 'Cancelled')
        exit
    }

    $steps = 3

    # --- Revit plugin (both scopes) ---
    Write-VxStep 1 $steps (T 'StepRemovePlugin')
    $totalRemoved = 0
    foreach ($ver in @("2023","2024","2025","2026","2027")) {
        foreach ($path in @(Remove-RevitAddin -Version $ver)) {
            Write-VxOk "Revit $ver" (Format-VxPath $path)
            $totalRemoved++
        }
    }
    if ($totalRemoved -eq 0) { Write-VxNote (T 'NoPlugin') }

    # --- MCP server ---
    Write-VxStep 2 $steps (T 'StepRemoveServer')
    $serverDir = Join-Path $env:USERPROFILE ".revitcortex\server"
    if (Test-Path $serverDir) {
        Remove-Item $serverDir -Recurse -Force
        Write-VxOk (T 'ServerRemoved') (Format-VxPath $serverDir)
    } else {
        Write-VxNote (T 'ServerNotFound')
    }

    # --- AI clients (the Claude Desktop entry is removed safely: other MCP servers stay) ---
    Write-VxStep 3 $steps (T 'StepRemoveClient')
    $configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
    try {
        $result = Remove-ClaudeMcpServer -ConfigPath $configPath -ServerName 'revitcortex'
        if ($result.Action -eq 'removed') {
            Write-VxOk (T 'DesktopRemoved')
            if ($result.BackupPath) { Write-VxNote (T 'DesktopBackup' (Format-VxPath $result.BackupPath)) }
        } else {
            Write-VxNote (T 'DesktopNotThere')
        }
    } catch {
        Write-VxWarn (T 'DesktopFailed' "$_")
    }

    $claudeCli = Get-Command claude -ErrorAction SilentlyContinue
    if ($claudeCli) {
        try { & claude mcp remove revitcortex 2>$null | Out-Null; Write-VxOk (T 'CodeRemoved') } catch {}
    }

    # --- Summary (user data is kept) ---
    $dataDir = Join-Path $env:USERPROFILE ".revitcortex"
    Show-VxSummary -Title (T 'SummaryRemoved') -Notes @((T 'DataKept' (Format-VxPath $dataDir))) `
        -NextTitle (T 'NextTitle') -Next @((T 'NextAfterRemove'))
    Wait-VxClose
} catch {
    Show-VxSummary -Title (T 'UninstallFailed') -Notes @("$($_.Exception.Message)") -Failed
    Wait-VxClose
    exit 1
}
