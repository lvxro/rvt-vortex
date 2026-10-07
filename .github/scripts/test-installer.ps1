#requires -Version 5.1
<#
.SYNOPSIS
    Installs RVT Vortex from a built package, checks what landed, and uninstalls
    it again. For the CI runner only: it really installs and really uninstalls.

.DESCRIPTION
    The runner has no Revit. Two empty add-in folders stand in for Revit 2025
    (installed for all users) and Revit 2027 (installed for this user only),
    which is all the installer looks at.

    Three runs, each with a time limit so a prompt nobody answers cannot hang
    the job:
      1. install.ps1 -Silent    the way the in-app updater runs it
      2. install.ps1            answering its questions through standard input
      3. uninstall.ps1          answering "y"

    What each run printed is attached to the job as a notice, so it can be read
    without downloading logs.
#>
param([Parameter(Mandatory)] [string] $Package)

$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_ACTIONS) {
    throw 'This installs and uninstalls for real. It is meant for the CI runner only.'
}

$Package = (Resolve-Path $Package).Path
# The scripts' output has accents and box characters: have the child processes
# write it as UTF-8, and read it as UTF-8.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:VORTEX_UI = 'plain'

function Send-Annotation {
    param([string] $Level, [string] $Title, [string] $Text)
    $encoded = $Text -replace '%', '%25' -replace "`r", '' -replace "`n", '%0A'
    Write-Host "::$Level title=$Title::$encoded"
}

function Invoke-Installer {
    # Runs one of the package's scripts; returns its exit code and what it printed.
    param(
        [string] $Script,
        [string] $Arguments = '',
        [string[]] $Answers = @(),
        [switch] $NonInteractive,
        [int] $TimeoutSeconds = 240
    )

    $engine = '-NoProfile -ExecutionPolicy Bypass'
    if ($NonInteractive) { $engine = "-NonInteractive $engine" }

    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'powershell.exe'
    $info.Arguments = "$engine -File `"$(Join-Path $Package $Script)`" $Arguments"
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    foreach ($answer in $Answers) { $process.StandardInput.WriteLine($answer) }
    $process.StandardInput.Close()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        return @{ Code = -1; Output = "TIMED OUT after $TimeoutSeconds s.`n" + $stdout.Result + $stderr.Result }
    }
    $process.WaitForExit()
    return @{ Code = $process.ExitCode; Output = ($stdout.Result + $stderr.Result) }
}

$problems = New-Object System.Collections.Generic.List[string]
function Expect {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { $problems.Add($Message) }
}

$machine2025 = 'C:\ProgramData\Autodesk\Revit\Addins\2025'
$machine2027 = 'C:\ProgramData\Autodesk\Revit\Addins\2027'
$user2027    = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'
$serverDir   = Join-Path $env:USERPROFILE '.revitcortex\server'
$serverExe   = Join-Path $serverDir 'RevitCortex.Server.exe'
$claudeJson  = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'

New-Item -ItemType Directory -Force $machine2025, $machine2027 | Out-Null

function Expect-Installed {
    param([string] $Run)
    Expect (Test-Path (Join-Path $machine2025 'RevitCortex\RevitCortex.Plugin.dll')) "${Run}: the plugin is not in the Revit 2025 all-users folder"
    Expect (Test-Path (Join-Path $machine2025 'RevitCortex.addin')) "${Run}: the Revit 2025 manifest is missing"
    Expect (Test-Path (Join-Path $user2027 'RevitCortex\RevitCortex.Plugin.dll')) "${Run}: the plugin is not in the Revit 2027 user folder"
    Expect (-not (Test-Path (Join-Path $machine2027 'RevitCortex'))) "${Run}: Revit 2027 got an all-users copy, which Revit 2027 ignores"
    Expect (Test-Path $serverExe) "${Run}: RevitCortex.Server.exe is missing"

    # The server is copied file by file: every file must be there, with its size.
    $sourceRoot = Join-Path $Package 'server'
    foreach ($file in (Get-ChildItem $sourceRoot -Recurse -File -Force)) {
        $copy = Join-Path $serverDir $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
        if (-not (Test-Path -LiteralPath $copy)) { $problems.Add("${Run}: server file not copied: $copy"); break }
        if ((Get-Item -LiteralPath $copy -Force).Length -ne $file.Length) { $problems.Add("${Run}: server file has another size: $copy"); break }
    }
    $copied = @(Get-ChildItem $serverDir -Recurse -File -Force).Count
    $expected = @(Get-ChildItem $sourceRoot -Recurse -File -Force).Count
    Expect ($copied -eq $expected) "${Run}: the server folder has $copied files, the package has $expected"
}

# 1. The way the updater runs it (UpdateChecker.LaunchInstaller).
$run = Invoke-Installer 'install.ps1' '-Silent' -NonInteractive
Send-Annotation 'notice' 'install.ps1 -Silent' $run.Output
Expect ($run.Code -eq 0) "install.ps1 -Silent exited with $($run.Code)"
Expect-Installed 'update'
Expect ((Test-Path $claudeJson) -and ((Get-Content $claudeJson -Raw) -match 'revitcortex')) 'update: Claude Desktop was not configured'

# 2. A person installing: no Defender exclusion, no client, then Enter to close.
#    (Where Defender is absent the first answer goes to the client question,
#    which reads it as "not now" as well.)
$run = Invoke-Installer 'install.ps1' '' @('n', '4', '')
Send-Annotation 'notice' 'install.ps1' $run.Output
Expect ($run.Code -eq 0) "install.ps1 exited with $($run.Code)"
Expect-Installed 'install'

# 3. Uninstall.
$run = Invoke-Installer 'uninstall.ps1' '' @('y', '')
Send-Annotation 'notice' 'uninstall.ps1' $run.Output
Expect ($run.Code -eq 0) "uninstall.ps1 exited with $($run.Code)"
Expect (-not (Test-Path (Join-Path $machine2025 'RevitCortex'))) 'uninstall: the Revit 2025 plugin is still there'
Expect (-not (Test-Path (Join-Path $user2027 'RevitCortex'))) 'uninstall: the Revit 2027 plugin is still there'
Expect (-not (Test-Path $serverDir)) 'uninstall: the server folder is still there'
Expect (-not ((Test-Path $claudeJson) -and ((Get-Content $claudeJson -Raw) -match 'revitcortex'))) 'uninstall: Claude Desktop still has the revitcortex entry'

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Send-Annotation 'error' 'Installer' $problem }
    exit 1
}
Write-Host 'The package installs, updates and uninstalls.'
