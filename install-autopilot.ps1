#requires -Version 5.0
<#
  Builds and installs RVT Vortex, a RevitCortex fork (Autopilot + Revit 2027 fix).

  What it does:
    1. Checks that Revit is closed.
    2. Checks for the .NET SDK (installs it with winget if missing).
    3. Detects which Revit versions have RevitCortex installed.
    4. Backs up the current installation to a folder on the Desktop.
    5. Builds and installs the new version with the project's deploy.ps1.

  Messages follow the Windows display language (English or Spanish).

  Usage: double-click INSTALL.bat (or right-click this file -> "Run with PowerShell").
  To roll back: see RESTORE-README.txt inside the backup folder.
#>
param(
    # Optional: force versions, e.g. -Versions 2025,2026
    [string[]]$Versions
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
if ($Versions) { $Versions = @($Versions | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

# --- Messages (English / Spanish, picked from the Windows display language) ---
$IsSpanish = [System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq "es"
$Msg = @{
    en = @{
        Elevating      = "Requesting administrator rights..."
        Title          = "=== RVT Vortex installer (RevitCortex fork with Autopilot) ==="
        RevitOpen      = "Revit is open. Save your work, close Revit and run this file again."
        RevitClosed    = "Revit is closed"
        NotFound       = "RevitCortex was not found in any Revit version."
        AskVersion     = "Type your Revit version (2023, 2024, 2025, 2026 or 2027)"
        BadVersion     = "Invalid version."
        VersionsFound  = "Revit versions to update: {0}"
        SdkMissing     = "The .NET {0} SDK is missing. Installing it with winget (this can take a few minutes)..."
        SdkFailed      = "Could not install the .NET SDK. Install it from https://dotnet.microsoft.com/download and run this again."
        SdkOk          = ".NET SDK available"
        Net48Missing   = "Revit 2023/2024 needs the .NET Framework 4.8 Developer Pack. Installing it..."
        BackupDone     = "Backup saved to: {0}"
        BackupFolder   = "RevitCortex-backup-{0}"
        RestoreFile    = "RESTORE-README.txt"
        RestoreText    = @"
RevitCortex backup made on {0}, before installing the Autopilot fork.

To go back to the previous version:
  1. Close Revit.
  2. Copy each folder here back to its original location, replacing files:
       ProgramData\<year>\  ->  C:\ProgramData\Autodesk\Revit\Addins\<year>\
       AppData\<year>\      ->  %APPDATA%\Autodesk\Revit\Addins\<year>\
  3. Open Revit.
"@
        Building       = "Building and installing for Revit {0} (the first time takes several minutes)..."
        BuildFailed    = "Installation failed for Revit {0}"
        Installed      = "Installed for Revit {0}"
        Errors         = "There were errors in: {0}. Copy the red text above and share it when asking for help."
        RollbackHint   = "Your previous installation is in the backup on the Desktop (see RESTORE-README.txt)."
        Done           = "=== Done ==="
        RestartClaude  = "If Claude Desktop was open, quit it completely and reopen it: the install restarts the RevitCortex server."
        OpenRevit      = "Open Revit: the 'RVT Vortex' panel (Add-Ins tab) has the 'Autopilot' button (grey = off, orange = on)."
        Guide          = "User guide: {0}"
        CopyError      = "Copy this error and share it when asking for help."
        PressEnter     = "Press Enter to close"
    }
    es = @{
        Elevating      = "Pidiendo permisos de administrador..."
        Title          = "=== Instalador de RVT Vortex (fork de RevitCortex con piloto automático) ==="
        RevitOpen      = "Revit está abierto. Guarde su trabajo, cierre Revit y vuelva a ejecutar este archivo."
        RevitClosed    = "Revit está cerrado"
        NotFound       = "No se encontró RevitCortex instalado en ninguna versión de Revit."
        AskVersion     = "Escriba su versión de Revit (2023, 2024, 2025, 2026 o 2027)"
        BadVersion     = "Versión no válida."
        VersionsFound  = "Versiones de Revit a actualizar: {0}"
        SdkMissing     = "Falta el SDK de .NET {0}. Instalándolo con winget (puede tardar unos minutos)..."
        SdkFailed      = "No se pudo instalar el SDK de .NET. Instálelo desde https://dotnet.microsoft.com/download y vuelva a ejecutar."
        SdkOk          = "SDK de .NET disponible"
        Net48Missing   = "Revit 2023/2024 necesita el paquete de desarrollo de .NET Framework 4.8. Instalándolo..."
        BackupDone     = "Respaldo guardado en: {0}"
        BackupFolder   = "RevitCortex-respaldo-{0}"
        RestoreFile    = "LEEME-RESTAURAR.txt"
        RestoreText    = @"
Respaldo de RevitCortex hecho el {0}, antes de instalar el fork con piloto automático.

Para volver a la versión anterior:
  1. Cierre Revit.
  2. Copie cada carpeta de acá a su lugar original, reemplazando los archivos:
       ProgramData\<año>\  ->  C:\ProgramData\Autodesk\Revit\Addins\<año>\
       AppData\<año>\      ->  %APPDATA%\Autodesk\Revit\Addins\<año>\
  3. Abra Revit.
"@
        Building       = "Compilando e instalando para Revit {0} (la primera vez tarda varios minutos)..."
        BuildFailed    = "Falló la instalación para Revit {0}"
        Installed      = "Instalado para Revit {0}"
        Errors         = "Hubo errores en: {0}. Copie el texto rojo de arriba y compártalo al pedir ayuda."
        RollbackHint   = "Su instalación anterior está en el respaldo del Escritorio (ver LEEME-RESTAURAR.txt)."
        Done           = "=== Listo ==="
        RestartClaude  = "Si Claude Desktop estaba abierto, ciérrelo del todo y vuelva a abrirlo: la instalación reinicia el servidor de RevitCortex."
        OpenRevit      = "Abra Revit: el panel 'RVT Vortex' (pestaña Add-Ins) tiene el botón 'Piloto automático' (gris = apagado, naranja = activo)."
        Guide          = "Guía de uso: {0}"
        CopyError      = "Copie este error y compártalo al pedir ayuda."
        PressEnter     = "Presione Enter para cerrar"
    }
}
$L = if ($IsSpanish) { $Msg.es } else { $Msg.en }

function Pause-End { Write-Host ""; Read-Host $L.PressEnter | Out-Null }
function Ok($m)    { Write-Host "  OK  $m" -ForegroundColor Green }
function Info($m)  { Write-Host "  ..  $m" -ForegroundColor Cyan }
function Fail($m)  { Write-Host "  !!  $m" -ForegroundColor Red }

# --- Elevate (deploy.ps1 cleans up C:\ProgramData) ---
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host $L.Elevating -ForegroundColor Yellow
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    if ($Versions) { $argList += @("-Versions", ($Versions -join ",")) }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    exit
}

try {
    Write-Host ""
    Write-Host $L.Title -ForegroundColor Cyan
    Write-Host ""

    # --- 1. Revit closed ---
    if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
        Fail $L.RevitOpen
        Pause-End; exit 1
    }
    Ok $L.RevitClosed

    # --- 2. Detect installed versions ---
    $all = "2023","2024","2025","2026","2027"
    if (-not $Versions) {
        $Versions = @()
        foreach ($v in $all) {
            $machine = "C:\ProgramData\Autodesk\Revit\Addins\$v\RevitCortex"
            $user    = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v\RevitCortex"
            if ((Test-Path $machine) -or (Test-Path $user)) { $Versions += $v }
        }
    }
    if (-not $Versions -or $Versions.Count -eq 0) {
        Fail $L.NotFound
        $v = Read-Host $L.AskVersion
        if ($all -notcontains $v) { Fail $L.BadVersion; Pause-End; exit 1 }
        $Versions = @($v)
    }
    Ok ($L.VersionsFound -f ($Versions -join ", "))

    # --- 3. .NET SDK ---
    $needNet10 = $Versions -contains "2027"
    $needNet48 = ($Versions -contains "2023") -or ($Versions -contains "2024")

    function Get-SdkMajors {
        try { (& dotnet --list-sdks 2>$null) | ForEach-Object { [int]($_.Split('.')[0]) } } catch { @() }
    }
    $sdks = @(Get-SdkMajors)
    $minimum = if ($needNet10) { 10 } else { 8 }
    if (-not ($sdks | Where-Object { $_ -ge $minimum })) {
        Info ($L.SdkMissing -f $minimum)
        $id = if ($needNet10) { "Microsoft.DotNet.SDK.10" } else { "Microsoft.DotNet.SDK.8" }
        winget install --id $id -e --accept-source-agreements --accept-package-agreements
        $env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")
        $sdks = @(Get-SdkMajors)
        if (-not ($sdks | Where-Object { $_ -ge $minimum })) {
            Fail $L.SdkFailed
            Pause-End; exit 1
        }
    }
    Ok $L.SdkOk

    if ($needNet48) {
        $pack = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
        if (-not (Test-Path $pack)) {
            Info $L.Net48Missing
            winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 -e --accept-source-agreements --accept-package-agreements
        }
    }

    # --- 4. Backup ---
    $stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
    $backup = Join-Path ([Environment]::GetFolderPath("Desktop")) ($L.BackupFolder -f $stamp)
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($v in $Versions) {
        $pairs = @(
            @{ Src = "C:\ProgramData\Autodesk\Revit\Addins\$v"; Dst = Join-Path $backup "ProgramData\$v" },
            @{ Src = (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"); Dst = Join-Path $backup "AppData\$v" }
        )
        foreach ($p in $pairs) {
            $dir = Join-Path $p.Src "RevitCortex"
            $man = Join-Path $p.Src "RevitCortex.addin"
            if (Test-Path $dir) {
                New-Item -ItemType Directory -Path $p.Dst -Force | Out-Null
                Copy-Item $dir $p.Dst -Recurse -Force
            }
            if (Test-Path $man) {
                New-Item -ItemType Directory -Path $p.Dst -Force | Out-Null
                Copy-Item $man $p.Dst -Force
            }
        }
    }
    ($L.RestoreText -f $stamp) | Set-Content -Path (Join-Path $backup $L.RestoreFile) -Encoding UTF8
    Ok ($L.BackupDone -f $backup)

    # --- 5. Build and install ---
    # deploy.ps1 installs Revit 2027+ to the per-user folder (%APPDATA%),
    # because Revit 2027 ignores all-users manifests in C:\ProgramData.
    $failed = @()
    foreach ($v in $Versions) {
        Write-Host ""
        Info ($L.Building -f $v)
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "deploy.ps1") -RevitVersion $v -Config Release
        if ($LASTEXITCODE -ne 0) { $failed += $v; Fail ($L.BuildFailed -f $v); continue }
        Ok ($L.Installed -f $v)
    }

    Write-Host ""
    if ($failed.Count -gt 0) {
        Fail ($L.Errors -f ($failed -join ", "))
        Fail $L.RollbackHint
    } else {
        Write-Host $L.Done -ForegroundColor Green
        Write-Host $L.RestartClaude
        Write-Host $L.OpenRevit
        Write-Host ($L.Guide -f (Join-Path $RepoRoot 'docs\AUTOPILOT.md'))
    }
}
catch {
    Fail $_.Exception.Message
    Fail $L.CopyError
}
Pause-End
