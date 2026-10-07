#requires -Version 5.1
<#
.SYNOPSIS
    The look of the RVT Vortex installer in the console: the spinning vortex,
    colors, step lines, progress bar, summary, and every text in English,
    Spanish and Italian.

.DESCRIPTION
    Three ways of drawing, picked once by Initialize-VxUi:

      live   a real console on Windows 10 1703 or later. 24-bit colors, the
             vortex spins, the progress bar redraws in place.
      ansi   the same colors with nothing redrawn (set VORTEX_UI=ansi; used to
             capture the screens as text).
      plain  Write-Host colors only. Output redirected to a file, an old
             console, or anything that failed while probing the console.

    Nothing here installs anything, and nothing here may stop an install: every
    console call that can throw is caught and falls back to plain text.

    Glyphs are limited to the ones every Windows console font has (the old
    code page 437 set): no emoji, no check mark outside that set.
#>

$script:Vx = @{
    Ansi   = $false    # 24-bit color escape sequences work
    Live   = $false    # the cursor can be moved: animation and in-place redraw
    Lang   = 'en'
    Width  = 80
}

$script:VxEsc   = [string][char]27
$script:VxReset = "$($script:VxEsc)[0m"

# The palette of the plugin's windows (UI/Theme.xaml), and the nearest console
# color for plain mode.
$script:VxTones = @{
    ink    = @{ Rgb = @(236, 238, 241); Color = 'White' }
    soft   = @{ Rgb = @(197, 203, 211); Color = 'Gray' }
    muted  = @{ Rgb = @(152, 161, 172); Color = 'DarkGray' }
    accent = @{ Rgb = @(217, 119, 87);  Color = 'DarkYellow' }
    glow   = @{ Rgb = @(240, 165, 141); Color = 'Yellow' }
    warn   = @{ Rgb = @(227, 179, 65);  Color = 'Yellow' }
    fail   = @{ Rgb = @(240, 98, 98);   Color = 'Red' }
}

$script:VxGlyph = @{
    Check = [string][char]0x221A    # square root sign: the check mark of code page 437
    Cross = 'x'
    Warn  = '!'
    Dot   = [string][char]0x00B7
    Full  = [string][char]0x2588
    Empty = [string][char]0x2591
    Rule  = [string][char]0x2500
}

# Where the step lines start, and where what belongs to a step starts.
$script:VxIndent     = '  '
$script:VxItemIndent = '       '

# ── Texts ────────────────────────────────────────────────────────────────

$script:VxText = @{
    en = @{
        Tagline            = 'AI for Autodesk Revit'
        SubInstall         = 'Installing version {0}'
        SubInstallPlain    = 'Installer'
        SubUpdate          = 'Updating to version {0}'
        SubUpdatePlain     = 'Update'
        SubUninstall       = 'Uninstaller'
        Elevating          = 'Asking Windows for administrator permission...'
        ElevationDenied    = 'Administrator permission is needed to install into Revit. Run the installer again and accept the Windows prompt.'
        StepChecks         = 'Checks'
        RevitClosed        = 'Revit is closed'
        WaitRevit          = 'Waiting for Revit to close'
        RevitStillOpen     = 'Revit is still open after {0} seconds. Close it and run the installer again.'
        RevitOpen          = 'Revit is open, and it keeps the plugin files locked.'
        RevitOpenPrompt    = 'Close Revit and press Enter (or type q to cancel):'
        Aborted            = 'Installation cancelled.'
        StepDetect         = 'Looking for Revit'
        NoRevit            = 'No supported Revit was found on this computer (2023 to 2027).'
        NotBundled         = 'Revit {0} is installed, but this package has no plugin for it.'
        SkippedRevit       = 'Revit {0} is installed too, but this package does not include it'
        PortBusy           = 'Port 8080 is in use by another program. You can change the port in Settings, inside Revit.'
        StepPlugin         = 'Installing the Revit plugin'
        AddinMissing       = 'RevitCortex.addin is missing from this package ({0}).'
        UserScope          = 'this user only'
        PluginFailed       = 'The plugin could not be installed for Revit {0}.'
        StepServer         = 'Installing the MCP server'
        ServerMissing      = 'The server files are missing from this package ({0}).'
        StoppingServer     = 'The MCP server was running and has been stopped. Restart your AI client afterwards.'
        ServerLocked       = '{0} could not be replaced: a file is still in use. Quit your AI client completely (Claude Desktop: tray icon > Quit) and run the installer again.'
        ServerExeMissing   = 'RevitCortex.Server.exe is missing from this package.'
        CopyFailed         = 'The server files could not be copied: {0}'
        Files              = '{0} files'
        ServerInstalled    = 'Server installed'
        DefenderPrompt     = 'Add a Windows Defender exclusion for the server folder? (y/N):'
        DefenderAdded      = 'Defender exclusion added'
        SkillInstalled     = 'Skill installed for {0}'
        StepGit            = 'Git'
        GitPresent         = 'Git is already installed'
        GitInstalling      = 'Git is missing. Installing it (this can take a minute)...'
        GitInstalled       = 'Git installed'
        GitFailed          = 'Git could not be installed. Get it from https://git-scm.com/download/win (Claude Code needs it).'
        StepClient         = 'Connecting your AI client'
        ClientQuestion     = 'How will you use RVT Vortex?'
        ClientBoth         = 'Both'
        ClientSkip         = 'Not now'
        ClientPrompt       = 'Choose 1-4:'
        DesktopBackup      = 'Previous Claude Desktop settings saved to {0}'
        DesktopDone        = 'Claude Desktop connected'
        DesktopFailed      = 'Claude Desktop could not be configured: {0}'
        DesktopUntouched   = 'Your current settings were left as they were. Fix the JSON and run the installer again.'
        CodeDone           = 'Claude Code connected'
        CodeMissing        = 'Claude Code is not installed. To connect it later, run:'
        CodeFailed         = 'Claude Code answered: {0}'
        ClientLater        = 'Nothing connected. To do it later:'
        ClientLaterDesktop = 'Claude Desktop: run this installer again'
        SummaryInstalled   = 'RVT Vortex is installed'
        SummaryUpdated     = 'RVT Vortex is up to date'
        RowPlugin          = 'Plugin'
        RowServer          = 'Server'
        RowGit             = 'Git'
        RowClient          = 'Client'
        GitOk              = 'ready'
        GitMissing         = 'not installed'
        NextTitle          = 'Next'
        NextRevit          = 'Open Revit and turn on Vortex Switch'
        NextClient         = 'Restart Claude Desktop or Claude Code'
        PressEnter         = 'Press Enter to close'
        ClosingIn          = 'This window closes in {0} s'
        FailedTitle        = 'The installation did not finish'
        UninstallConfirm   = 'This removes RVT Vortex from every Revit version. Continue? (y/N):'
        Cancelled          = 'Cancelled.'
        StepRemovePlugin   = 'Removing the Revit plugin'
        NoPlugin           = 'No plugin was installed'
        StepRemoveServer   = 'Removing the MCP server'
        ServerRemoved      = 'Server removed'
        ServerNotFound     = 'The server was not installed'
        StepRemoveClient   = 'Disconnecting your AI clients'
        DesktopRemoved     = 'Claude Desktop disconnected'
        DesktopNotThere    = 'Claude Desktop was not connected'
        CodeRemoved        = 'Claude Code disconnected'
        SummaryRemoved     = 'RVT Vortex was removed'
        DataKept           = 'Your settings and logs were kept in {0}. Delete that folder if you no longer need it.'
        NextAfterRemove    = 'Restart Revit and your AI client'
        UninstallFailed    = 'The uninstall did not finish'
    }
    es = @{
        Tagline            = 'IA para Autodesk Revit'
        SubInstall         = 'Instalando la versión {0}'
        SubInstallPlain    = 'Instalador'
        SubUpdate          = 'Actualizando a la versión {0}'
        SubUpdatePlain     = 'Actualización'
        SubUninstall       = 'Desinstalador'
        Elevating          = 'Pidiendo permiso de administrador a Windows...'
        ElevationDenied    = 'Hace falta permiso de administrador para instalar en Revit. Vuelva a ejecutar el instalador y acepte el aviso de Windows.'
        StepChecks         = 'Comprobaciones'
        RevitClosed        = 'Revit está cerrado'
        WaitRevit          = 'Esperando a que Revit se cierre'
        RevitStillOpen     = 'Revit sigue abierto después de {0} segundos. Ciérrelo y vuelva a ejecutar el instalador.'
        RevitOpen          = 'Revit está abierto y mantiene bloqueados los archivos del plugin.'
        RevitOpenPrompt    = 'Cierre Revit y presione Enter (o escriba q para cancelar):'
        Aborted            = 'Instalación cancelada.'
        StepDetect         = 'Buscando Revit'
        NoRevit            = 'No se encontró ningún Revit compatible en este equipo (2023 a 2027).'
        NotBundled         = 'Revit {0} está instalado, pero este paquete no trae plugin para esa versión.'
        SkippedRevit       = 'Revit {0} también está instalado, pero este paquete no lo incluye'
        PortBusy           = 'El puerto 8080 está ocupado por otro programa. Puede cambiarlo en Configuración, dentro de Revit.'
        StepPlugin         = 'Instalando el plugin de Revit'
        AddinMissing       = 'Falta RevitCortex.addin en este paquete ({0}).'
        UserScope          = 'solo este usuario'
        PluginFailed       = 'No se pudo instalar el plugin para Revit {0}.'
        StepServer         = 'Instalando el servidor MCP'
        ServerMissing      = 'Faltan los archivos del servidor en este paquete ({0}).'
        StoppingServer     = 'El servidor MCP estaba en uso y se detuvo. Reinicie su cliente de IA al terminar.'
        ServerLocked       = 'No se pudo reemplazar {0}: hay un archivo en uso. Cierre por completo su cliente de IA (Claude Desktop: icono de la bandeja > Salir) y vuelva a ejecutar el instalador.'
        ServerExeMissing   = 'Falta RevitCortex.Server.exe en este paquete.'
        CopyFailed         = 'No se pudieron copiar los archivos del servidor: {0}'
        Files              = '{0} archivos'
        ServerInstalled    = 'Servidor instalado'
        DefenderPrompt     = '¿Agregar una exclusión de Windows Defender para la carpeta del servidor? (s/N):'
        DefenderAdded      = 'Exclusión de Defender agregada'
        SkillInstalled     = 'Skill instalada para {0}'
        StepGit            = 'Git'
        GitPresent         = 'Git ya está instalado'
        GitInstalling      = 'Falta Git. Instalándolo (puede tardar un minuto)...'
        GitInstalled       = 'Git instalado'
        GitFailed          = 'No se pudo instalar Git. Descárguelo de https://git-scm.com/download/win (Claude Code lo necesita).'
        StepClient         = 'Conectando su cliente de IA'
        ClientQuestion     = '¿Cómo va a usar RVT Vortex?'
        ClientBoth         = 'Los dos'
        ClientSkip         = 'Ahora no'
        ClientPrompt       = 'Elija 1-4:'
        DesktopBackup      = 'La configuración anterior de Claude Desktop quedó guardada en {0}'
        DesktopDone        = 'Claude Desktop conectado'
        DesktopFailed      = 'No se pudo configurar Claude Desktop: {0}'
        DesktopUntouched   = 'Su configuración actual quedó como estaba. Corrija el JSON y vuelva a ejecutar el instalador.'
        CodeDone           = 'Claude Code conectado'
        CodeMissing        = 'Claude Code no está instalado. Para conectarlo más adelante, ejecute:'
        CodeFailed         = 'Claude Code respondió: {0}'
        ClientLater        = 'No se conectó nada. Para hacerlo más adelante:'
        ClientLaterDesktop = 'Claude Desktop: vuelva a ejecutar este instalador'
        SummaryInstalled   = 'RVT Vortex quedó instalado'
        SummaryUpdated     = 'RVT Vortex quedó actualizado'
        RowPlugin          = 'Plugin'
        RowServer          = 'Servidor'
        RowGit             = 'Git'
        RowClient          = 'Cliente'
        GitOk              = 'listo'
        GitMissing         = 'sin instalar'
        NextTitle          = 'Qué sigue'
        NextRevit          = 'Abra Revit y encienda Vortex Switch'
        NextClient         = 'Reinicie Claude Desktop o Claude Code'
        PressEnter         = 'Presione Enter para cerrar'
        ClosingIn          = 'Esta ventana se cierra en {0} s'
        FailedTitle        = 'La instalación no terminó'
        UninstallConfirm   = 'Esto quita RVT Vortex de todas las versiones de Revit. ¿Continuar? (s/N):'
        Cancelled          = 'Cancelado.'
        StepRemovePlugin   = 'Quitando el plugin de Revit'
        NoPlugin           = 'No había ningún plugin instalado'
        StepRemoveServer   = 'Quitando el servidor MCP'
        ServerRemoved      = 'Servidor quitado'
        ServerNotFound     = 'El servidor no estaba instalado'
        StepRemoveClient   = 'Desconectando sus clientes de IA'
        DesktopRemoved     = 'Claude Desktop desconectado'
        DesktopNotThere    = 'Claude Desktop no estaba conectado'
        CodeRemoved        = 'Claude Code desconectado'
        SummaryRemoved     = 'RVT Vortex fue desinstalado'
        DataKept           = 'Su configuración y sus registros siguen en {0}. Borre esa carpeta si ya no los necesita.'
        NextAfterRemove    = 'Reinicie Revit y su cliente de IA'
        UninstallFailed    = 'La desinstalación no terminó'
    }
    it = @{
        Tagline            = 'IA per Autodesk Revit'
        SubInstall         = 'Installazione della versione {0}'
        SubInstallPlain    = 'Installazione'
        SubUpdate          = 'Aggiornamento alla versione {0}'
        SubUpdatePlain     = 'Aggiornamento'
        SubUninstall       = 'Disinstallazione'
        Elevating          = 'Richiesta a Windows dei permessi di amministratore...'
        ElevationDenied    = 'Servono i permessi di amministratore per installare in Revit. Esegui di nuovo il programma e accetta la richiesta di Windows.'
        StepChecks         = 'Controlli'
        RevitClosed        = 'Revit è chiuso'
        WaitRevit          = 'In attesa della chiusura di Revit'
        RevitStillOpen     = 'Revit è ancora aperto dopo {0} secondi. Chiudilo ed esegui di nuovo il programma.'
        RevitOpen          = 'Revit è aperto e tiene bloccati i file del plugin.'
        RevitOpenPrompt    = 'Chiudi Revit e premi Invio (o scrivi q per annullare):'
        Aborted            = 'Installazione annullata.'
        StepDetect         = 'Ricerca di Revit'
        NoRevit            = 'Nessun Revit supportato trovato su questo computer (dal 2023 al 2027).'
        NotBundled         = 'Revit {0} è installato, ma questo pacchetto non contiene il plugin per quella versione.'
        SkippedRevit       = 'Anche Revit {0} è installato, ma questo pacchetto non lo include'
        PortBusy           = 'La porta 8080 è usata da un altro programma. Puoi cambiarla in Impostazioni, dentro Revit.'
        StepPlugin         = 'Installazione del plugin di Revit'
        AddinMissing       = 'Manca RevitCortex.addin in questo pacchetto ({0}).'
        UserScope          = 'solo questo utente'
        PluginFailed       = 'Impossibile installare il plugin per Revit {0}.'
        StepServer         = 'Installazione del server MCP'
        ServerMissing      = 'Mancano i file del server in questo pacchetto ({0}).'
        StoppingServer     = 'Il server MCP era in uso ed è stato arrestato. Riavvia il client IA al termine.'
        ServerLocked       = 'Impossibile sostituire {0}: un file è ancora in uso. Chiudi del tutto il client IA (Claude Desktop: icona nella barra > Esci) ed esegui di nuovo il programma.'
        ServerExeMissing   = 'Manca RevitCortex.Server.exe in questo pacchetto.'
        CopyFailed         = 'Impossibile copiare i file del server: {0}'
        Files              = '{0} file'
        ServerInstalled    = 'Server installato'
        DefenderPrompt     = 'Aggiungere un''esclusione di Windows Defender per la cartella del server? (s/N):'
        DefenderAdded      = 'Esclusione di Defender aggiunta'
        SkillInstalled     = 'Skill installata per {0}'
        StepGit            = 'Git'
        GitPresent         = 'Git è già installato'
        GitInstalling      = 'Git non è presente. Installazione in corso (può richiedere un minuto)...'
        GitInstalled       = 'Git installato'
        GitFailed          = 'Impossibile installare Git. Scaricalo da https://git-scm.com/download/win (serve a Claude Code).'
        StepClient         = 'Collegamento del client IA'
        ClientQuestion     = 'Come userai RVT Vortex?'
        ClientBoth         = 'Entrambi'
        ClientSkip         = 'Non ora'
        ClientPrompt       = 'Scegli 1-4:'
        DesktopBackup      = 'Le impostazioni precedenti di Claude Desktop sono state salvate in {0}'
        DesktopDone        = 'Claude Desktop collegato'
        DesktopFailed      = 'Impossibile configurare Claude Desktop: {0}'
        DesktopUntouched   = 'Le impostazioni attuali non sono state toccate. Correggi il JSON ed esegui di nuovo il programma.'
        CodeDone           = 'Claude Code collegato'
        CodeMissing        = 'Claude Code non è installato. Per collegarlo più tardi, esegui:'
        CodeFailed         = 'Claude Code ha risposto: {0}'
        ClientLater        = 'Nessun client collegato. Per farlo più tardi:'
        ClientLaterDesktop = 'Claude Desktop: esegui di nuovo questo programma'
        SummaryInstalled   = 'RVT Vortex è installato'
        SummaryUpdated     = 'RVT Vortex è aggiornato'
        RowPlugin          = 'Plugin'
        RowServer          = 'Server'
        RowGit             = 'Git'
        RowClient          = 'Client'
        GitOk              = 'pronto'
        GitMissing         = 'non installato'
        NextTitle          = 'Prossimi passi'
        NextRevit          = 'Apri Revit e accendi Vortex Switch'
        NextClient         = 'Riavvia Claude Desktop o Claude Code'
        PressEnter         = 'Premi Invio per chiudere'
        ClosingIn          = 'Questa finestra si chiude tra {0} s'
        FailedTitle        = 'L''installazione non è terminata'
        UninstallConfirm   = 'Questo rimuove RVT Vortex da tutte le versioni di Revit. Continuare? (s/N):'
        Cancelled          = 'Annullato.'
        StepRemovePlugin   = 'Rimozione del plugin di Revit'
        NoPlugin           = 'Nessun plugin installato'
        StepRemoveServer   = 'Rimozione del server MCP'
        ServerRemoved      = 'Server rimosso'
        ServerNotFound     = 'Il server non era installato'
        StepRemoveClient   = 'Scollegamento dei client IA'
        DesktopRemoved     = 'Claude Desktop scollegato'
        DesktopNotThere    = 'Claude Desktop non era collegato'
        CodeRemoved        = 'Claude Code scollegato'
        SummaryRemoved     = 'RVT Vortex è stato rimosso'
        DataKept           = 'Impostazioni e registri sono rimasti in {0}. Elimina quella cartella se non ti servono più.'
        NextAfterRemove    = 'Riavvia Revit e il client IA'
        UninstallFailed    = 'La disinstallazione non è terminata'
    }
}

function Get-VxText {
    <#
    .SYNOPSIS
        A text in the installer's language (English when the key has no translation).
    #>
    param(
        [Parameter(Mandatory)] [string] $Key,
        [object[]] $Arguments = @()
    )

    $table = $script:VxText[$script:Vx.Lang]
    if (-not $table -or -not $table.ContainsKey($Key)) { $table = $script:VxText['en'] }
    if (-not $table.ContainsKey($Key)) { return $Key }

    $text = $table[$Key]
    if ($Arguments.Count -eq 0) { return $text }
    try { return ($text -f $Arguments) } catch { return $text }
}

function Test-VxYes {
    <#
    .SYNOPSIS
        True for "y", "yes", "s", "si", "sì", "sí": yes in the three languages.
    #>
    param([string] $Answer)
    return ("$Answer".Trim() -match '^(y|yes|s|si|s[ìí])$')
}

# ── Starting up ──────────────────────────────────────────────────────────

function Enable-VxVirtualTerminal {
    # pwsh on Linux and macOS (where the tests of this file can run) always
    # understands escape sequences.
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $true }

    # 24-bit color arrived with Windows 10 1703 (build 15063).
    if ([Environment]::OSVersion.Version.Build -lt 15063) { return $false }

    try {
        if (-not ('VortexUi.NativeConsole' -as [type])) {
            Add-Type -Namespace VortexUi -Name NativeConsole -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GetStdHandle(int nStdHandle);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
'@
        }

        $handle = [VortexUi.NativeConsole]::GetStdHandle(-11)    # STD_OUTPUT_HANDLE
        [uint32] $mode = 0
        if (-not [VortexUi.NativeConsole]::GetConsoleMode($handle, [ref] $mode)) { return $false }

        $virtualTerminal = [uint32] 4                            # ENABLE_VIRTUAL_TERMINAL_PROCESSING
        if (($mode -band $virtualTerminal) -ne 0) { return $true }
        return [VortexUi.NativeConsole]::SetConsoleMode($handle, [uint32] ($mode -bor $virtualTerminal))
    } catch {
        # Add-Type is not allowed everywhere (constrained language mode).
        try { return [bool] $Host.UI.SupportsVirtualTerminal } catch { return $false }
    }
}

function Initialize-VxUi {
    <#
    .SYNOPSIS
        Picks how to draw (live, ansi or plain) and the language. Call it once,
        before anything is written.
    .PARAMETER Language
        en, es or it. Default: the Windows display language.
    #>
    param([string] $Language)

    $ansi = $false
    $live = $false
    $width = 80

    $forced = "$env:VORTEX_UI".ToLowerInvariant()
    if ($forced -eq 'ansi') {
        $ansi = $true
    } elseif ($forced -ne 'plain') {
        try {
            if (-not [Console]::IsOutputRedirected) {
                $width = [Console]::WindowWidth
                $ansi = [bool] (Enable-VxVirtualTerminal)
                # The animation redraws about 23 lines in place: it needs a
                # window that shows all of them.
                $live = $ansi -and $width -ge 64 -and [Console]::WindowHeight -ge 26
            }
        } catch {
            $ansi = $false
            $live = $false
        }
    }

    if (-not $Language) {
        try { $Language = "$PSUICulture" } catch { $Language = '' }
        if (-not $Language) { try { $Language = (Get-UICulture).Name } catch { $Language = 'en' } }
    }
    $lang = 'en'
    if ("$Language".Length -ge 2) {
        $two = "$Language".Substring(0, 2).ToLowerInvariant()
        if ($script:VxText.ContainsKey($two)) { $lang = $two }
    }

    $script:Vx.Ansi = $ansi
    $script:Vx.Live = $live
    $script:Vx.Lang = $lang
    $script:Vx.Width = $width
}

# ── Writing ──────────────────────────────────────────────────────────────

function Get-VxSgr {
    # The escape sequence that switches to a tone's color.
    param([string] $Tone)
    $entry = $script:VxTones[$Tone]
    if (-not $entry) { $entry = $script:VxTones['ink'] }
    $rgb = $entry.Rgb
    return "$($script:VxEsc)[38;2;$($rgb[0]);$($rgb[1]);$($rgb[2])m"
}

function Format-VxSegments {
    # 'tone', 'text', 'tone', 'text'... as one string with color escapes.
    param([object[]] $Segments = @())
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i + 1 -lt $Segments.Count; $i += 2) {
        [void] $sb.Append((Get-VxSgr ([string] $Segments[$i]))).Append([string] $Segments[$i + 1])
    }
    [void] $sb.Append($script:VxReset)
    return $sb.ToString()
}

function Write-Vx {
    <#
    .SYNOPSIS
        Writes one line made of colored pieces: Write-Vx 'accent', ' 1/6 ', 'ink', 'Checks'.
        With no pieces, an empty line.
    #>
    param(
        [object[]] $Segments = @(),
        [switch] $NoNewline
    )

    if ($script:Vx.Ansi) {
        Write-Host (Format-VxSegments $Segments) -NoNewline:$NoNewline
        return
    }

    for ($i = 0; $i + 1 -lt $Segments.Count; $i += 2) {
        $entry = $script:VxTones[[string] $Segments[$i]]
        if (-not $entry) { $entry = $script:VxTones['ink'] }
        Write-Host ([string] $Segments[$i + 1]) -ForegroundColor $entry.Color -NoNewline
    }
    if (-not $NoNewline) { Write-Host '' }
}

function Write-VxStep {
    # "  2/6  Looking for Revit", with an empty line above.
    param([int] $Number, [int] $Total, [string] $Text)
    Write-Vx
    Write-Vx 'accent', ("{0}{1}/{2}  " -f $script:VxIndent, $Number, $Total), 'ink', $Text
}

function Write-VxOk {
    # Something that went well. Detail is shown dimmer, after the text.
    param([string] $Text, [string] $Detail)
    $segments = @('accent', "$($script:VxItemIndent)$($script:VxGlyph.Check) ", 'soft', $Text)
    if ($Detail) { $segments += @('muted', "   $Detail") }
    Write-Vx $segments
}

function Split-VxText {
    <#
    .SYNOPSIS
        Breaks a sentence into lines that fit the window after an indent, so
        the lines that follow can be indented too instead of starting at the
        left edge. A word longer than a line (a path) is left whole.
    #>
    param([string] $Text, [int] $Indent)

    $max = [Math]::Max(30, [Math]::Min($script:Vx.Width, 110) - $Indent - 1)
    $lines = New-Object System.Collections.Generic.List[string]
    $line = ''
    foreach ($word in ("$Text" -split ' ')) {
        if ($line.Length -eq 0) { $line = $word }
        elseif ($line.Length + 1 + $word.Length -gt $max) { $lines.Add($line); $line = $word }
        else { $line = "$line $word" }
    }
    if ($line.Length -gt 0 -or $lines.Count -eq 0) { $lines.Add($line) }
    return , $lines.ToArray()
}

function Write-VxItem {
    # A marked line under a step; a long text continues under itself.
    param([string] $MarkTone, [string] $Mark, [string] $TextTone, [string] $Text)
    $first = $true
    foreach ($line in (Split-VxText $Text ($script:VxItemIndent.Length + 2))) {
        if ($first) { Write-Vx $MarkTone, "$($script:VxItemIndent)$Mark ", $TextTone, $line }
        else { Write-Vx $TextTone, "$($script:VxItemIndent)  $line" }
        $first = $false
    }
}

function Write-VxNote {
    # A plain remark under a step.
    param([string] $Text)
    Write-VxItem 'muted' $script:VxGlyph.Dot 'muted' $Text
}

function Write-VxWarn {
    param([string] $Text)
    Write-VxItem 'warn' $script:VxGlyph.Warn 'soft' $Text
}

function Write-VxFail {
    param([string] $Text)
    Write-VxItem 'fail' $script:VxGlyph.Cross 'ink' $Text
}

function Format-VxPath {
    <#
    .SYNOPSIS
        A path as shown on screen: the user's folders become %APPDATA% and
        %USERPROFILE%, which is shorter and can still be pasted into Explorer.
        Never use the result as a path in a command.
    #>
    param([string] $Path)
    if (-not $Path) { return '' }
    foreach ($name in @('APPDATA', 'LOCALAPPDATA', 'USERPROFILE')) {
        $root = [Environment]::GetEnvironmentVariable($name)
        if ($root -and $Path.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return "%$name%" + $Path.Substring($root.TrimEnd('\').Length)
        }
    }
    return $Path
}

function Read-VxLine {
    <#
    .SYNOPSIS
        Asks a question under a step and returns what was typed. Only for an
        interactive run: Read-Host throws under powershell -NonInteractive.
    #>
    param([string] $Prompt)
    Write-Vx 'glow', "$($script:VxItemIndent)$Prompt " -NoNewline
    return (Read-Host)
}

# ── The vortex ───────────────────────────────────────────────────────────

# Darkest to brightest. Index 0 is "nothing".
$script:VxRamp = ' .:-=+*#%@'
$script:VxVortexWidth  = 39
$script:VxVortexHeight = 17
$script:VxVortexIndent = '      '
$script:VxMap = $null
$script:VxLevelSgr = $null

function Get-VxVortexMap {
    <#
    .SYNOPSIS
        Per character cell, what does not change from frame to frame: the angle
        of the three-armed spiral at that cell, and how bright the cell can get
        (fading to nothing at the rim and at the eye).
    #>
    if ($script:VxMap) { return $script:VxMap }

    $w = $script:VxVortexWidth
    $h = $script:VxVortexHeight
    $eye = 0.14      # radius of the empty center
    $twist = 2.2     # how much the arms curl from the center to the rim

    $angle = New-Object 'double[,]' $h, $w
    $fall = New-Object 'double[,]' $h, $w
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            # A cell is about twice as tall as wide: 39 x 17 cells is a circle.
            $nx = ($x - ($w - 1) / 2.0) / (($w - 1) / 2.0)
            $ny = ($y - ($h - 1) / 2.0) / (($h - 1) / 2.0)
            $r = [Math]::Sqrt($nx * $nx + $ny * $ny)
            if ($r -gt 1.0 -or $r -lt $eye) { continue }

            $angle[$y, $x] = 3.0 * ([Math]::Atan2($ny, $nx) - $twist * $r)
            $rim = [Math]::Min(1.0, (1.0 - $r) / 0.45)
            $core = [Math]::Min(1.0, ($r - $eye) / 0.10 + 0.35)
            $fall[$y, $x] = $rim * $core
        }
    }

    # One color per brightness level: dark clay, the accent, a pale glow.
    $dark = @(92, 48, 34); $mid = @(217, 119, 87); $light = @(255, 214, 194)
    $sgr = New-Object 'string[]' 10
    for ($level = 1; $level -le 9; $level++) {
        $v = $level / 9.0
        if ($v -lt 0.65) { $from = $dark; $to = $mid; $t = $v / 0.65 }
        else { $from = $mid; $to = $light; $t = ($v - 0.65) / 0.35 }
        $rgb = 0..2 | ForEach-Object { [int] [Math]::Round($from[$_] + ($to[$_] - $from[$_]) * $t) }
        $sgr[$level] = "$($script:VxEsc)[38;2;$($rgb[0]);$($rgb[1]);$($rgb[2])m"
    }

    $script:VxLevelSgr = $sgr
    $script:VxMap = @{ Width = $w; Height = $h; Angle = $angle; Fall = $fall }
    return $script:VxMap
}

function Get-VxVortexFrame {
    <#
    .SYNOPSIS
        The lines of the vortex at one moment.
    .PARAMETER Phase
        Where the arms are. Adding 2*pi brings the same picture back, one arm on.
    .PARAMETER Intensity
        0 (nothing) to 1 (full): used to fade the vortex in.
    .PARAMETER Color
        With color escapes; without, plain characters.
    #>
    param(
        [double] $Phase = 0.0,
        [double] $Intensity = 1.0,
        [switch] $Color
    )

    $map = Get-VxVortexMap
    $w = $map.Width; $h = $map.Height
    $angle = $map.Angle; $fall = $map.Fall
    $ramp = $script:VxRamp
    $sgr = $script:VxLevelSgr

    $lines = New-Object 'string[]' $h
    $sb = New-Object System.Text.StringBuilder
    for ($y = 0; $y -lt $h; $y++) {
        [void] $sb.Clear()
        [void] $sb.Append($script:VxVortexIndent)
        $last = -1
        for ($x = 0; $x -lt $w; $x++) {
            $f = $fall[$y, $x]
            if ($f -le 0) { [void] $sb.Append(' '); continue }
            $v = [Math]::Cos($angle[$y, $x] + $Phase)
            if ($v -le 0) { [void] $sb.Append(' '); continue }

            $level = [int] [Math]::Floor($v * $f * $Intensity * 9.0 + 0.5)
            if ($level -le 0) { [void] $sb.Append(' '); continue }
            if ($level -gt 9) { $level = 9 }
            if ($Color -and $level -ne $last) { [void] $sb.Append($sgr[$level]); $last = $level }
            [void] $sb.Append($ramp[$level])
        }
        if ($Color) { [void] $sb.Append($script:VxReset) }
        $lines[$y] = $sb.ToString().TrimEnd()
    }
    return $lines
}

function Get-VxCentered {
    # Spaces that center a text under the vortex.
    param([string] $Text)
    $pad = [int] [Math]::Floor(($script:VxVortexWidth - $Text.Length) / 2.0)
    if ($pad -lt 0) { $pad = 0 }
    return $script:VxVortexIndent + (' ' * $pad)
}

function Show-VxBanner {
    <#
    .SYNOPSIS
        The vortex, the product name and a line under it. In a live console
        the vortex spins in; with -While it keeps spinning until that script
        block returns false (the update waits for Revit to close this way).
    .PARAMETER While
        Keep spinning while this returns true. Checked a few times per second.
    .PARAMETER WhileText
        What is being waited for, shown under the title during the wait.
    .PARAMETER TimeoutSeconds
        Give up waiting after this long (0 = never).
    .OUTPUTS
        False when the wait timed out; true otherwise.
    #>
    param(
        [string] $Subtitle,
        [scriptblock] $While,
        [string] $WhileText,
        [int] $TimeoutSeconds = 0
    )

    $name = 'R V T   V O R T E X'
    $nameLine = @('ink', ((Get-VxCentered $name) + $name))
    $subLine = @('muted', ((Get-VxCentered $Subtitle) + $Subtitle))
    $restPhase = 0.0
    $timedOut = $false

    if (-not $script:Vx.Live) {
        # No animation: draw it once, and wait without drawing.
        Write-Vx
        if ($script:Vx.Ansi) {
            foreach ($line in (Get-VxVortexFrame -Phase $restPhase -Color)) { Write-Host $line }
        } else {
            foreach ($line in (Get-VxVortexFrame -Phase $restPhase)) {
                Write-Host $line -ForegroundColor $script:VxTones['accent'].Color
            }
        }
        Write-Vx
        Write-Vx $nameLine
        Write-Vx $subLine

        if ($While) {
            $started = Get-Date
            $announced = $false
            while (& $While) {
                if (-not $announced) { Write-Vx; Write-VxNote "$WhileText..."; $announced = $true }
                if ($TimeoutSeconds -gt 0 -and ((Get-Date) - $started).TotalSeconds -ge $TimeoutSeconds) {
                    $timedOut = $true
                    break
                }
                Start-Sleep -Milliseconds 500
            }
        }
        return (-not $timedOut)
    }

    $esc = $script:VxEsc
    $out = [Console]::Out
    $eraseToEnd = "$esc[K"
    $height = $script:VxVortexHeight
    # The block that is redrawn: an empty line, the vortex, an empty line,
    # the name, the subtitle, an empty line, the status line.
    $blockHeight = $height + 6

    $drawBlock = {
        param([double] $phase, [double] $intensity, [string] $status)
        $sb = New-Object System.Text.StringBuilder
        [void] $sb.Append($eraseToEnd).Append("`r`n")
        foreach ($line in (Get-VxVortexFrame -Phase $phase -Intensity $intensity -Color)) {
            [void] $sb.Append($line).Append($eraseToEnd).Append("`r`n")
        }
        [void] $sb.Append($eraseToEnd).Append("`r`n")
        [void] $sb.Append((Format-VxSegments $nameLine)).Append($eraseToEnd).Append("`r`n")
        [void] $sb.Append((Format-VxSegments $subLine)).Append($eraseToEnd).Append("`r`n")
        [void] $sb.Append($eraseToEnd).Append("`r`n")
        [void] $sb.Append($status).Append($eraseToEnd).Append("`r`n")
        $out.Write($sb.ToString())
        $out.Flush()
    }
    $backToTop = "$esc[$($blockHeight)A`r"

    try {
        $out.Write("$esc[?25l")    # hide the cursor while drawing

        # Spin in: two arm-steps of rotation that slow down, fading in.
        $spinIn = 1.3
        $turn = 4.0 * [Math]::PI
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $first = $true
        while ($true) {
            $t = $clock.Elapsed.TotalSeconds / $spinIn
            if ($t -ge 1.0) { break }
            $eased = 1.0 - [Math]::Pow(1.0 - $t, 3)
            if (-not $first) { $out.Write($backToTop) }
            & $drawBlock ($restPhase - $turn * (1.0 - $eased)) ([Math]::Min(1.0, $t / 0.4)) ''
            $first = $false
            Start-Sleep -Milliseconds 25
        }

        # Keep turning while waiting.
        if ($While) {
            $phase = $restPhase
            $waiting = [System.Diagnostics.Stopwatch]::StartNew()
            $sinceCheck = [System.Diagnostics.Stopwatch]::StartNew()
            $stillWaiting = [bool] (& $While)
            while ($stillWaiting) {
                $seconds = [int] $waiting.Elapsed.TotalSeconds
                if ($TimeoutSeconds -gt 0 -and $seconds -ge $TimeoutSeconds) { $timedOut = $true; break }

                $phase += 0.24
                $status = Format-VxSegments @(
                    'accent', "$($script:VxIndent)$($script:VxGlyph.Dot) ",
                    'soft', $WhileText,
                    'muted', ("   {0} s" -f $seconds))
                if (-not $first) { $out.Write($backToTop) }
                & $drawBlock $phase 1.0 $status
                $first = $false
                Start-Sleep -Milliseconds 30

                if ($sinceCheck.ElapsedMilliseconds -ge 300) {
                    $stillWaiting = [bool] (& $While)
                    $sinceCheck.Restart()
                }
            }
        }

        # Come to rest.
        if (-not $first) { $out.Write($backToTop) }
        & $drawBlock $restPhase 1.0 ''
        # The status line is empty again: hand it back to whatever comes next.
        $out.Write("$esc[2A`r")
    } catch {
        # Drawing must never stop the install.
    } finally {
        try { $out.Write("$esc[?25h"); $out.Flush() } catch { }
    }

    return (-not $timedOut)
}

# ── Progress, summary, closing ───────────────────────────────────────────

function Format-VxBar {
    # "       ████████░░░░░░░░   43 %   148 files" as segments.
    param([int] $Done, [int] $Total, [string] $Text)
    $cells = 28
    $ratio = 1.0
    if ($Total -gt 0) { $ratio = [Math]::Min(1.0, [Math]::Max(0.0, $Done / [double] $Total)) }
    $filled = [int] [Math]::Floor($ratio * $cells + 0.5)
    return @(
        'accent', ($script:VxItemIndent + ($script:VxGlyph.Full * $filled)),
        'muted', ($script:VxGlyph.Empty * ($cells - $filled)),
        'soft', ("  {0,3} %" -f [int] [Math]::Floor($ratio * 100)),
        'muted', "   $Text")
}

function Write-VxProgress {
    <#
    .SYNOPSIS
        Redraws the progress bar on the current line. Does nothing where a
        line cannot be redrawn: Complete-VxProgress prints the bar once.
    #>
    param([int] $Done, [int] $Total, [string] $Text)
    if (-not $script:Vx.Live) { return }
    try {
        [Console]::Out.Write("`r" + (Format-VxSegments (Format-VxBar $Done $Total $Text)) + "$($script:VxEsc)[K")
    } catch { }
}

function Complete-VxProgress {
    # The bar at 100 %, as a line that stays.
    param([int] $Total, [string] $Text)
    if ($script:Vx.Live) {
        try { [Console]::Out.Write("`r$($script:VxEsc)[2K") } catch { }
    }
    Write-Vx (Format-VxBar $Total $Total $Text)
}

function Show-VxSummary {
    <#
    .SYNOPSIS
        The closing block: a rule, what happened, a few label / value rows,
        and what to do next.
    .PARAMETER Rows
        Pairs: @(@('Plugin', 'Revit 2025'), @('Git', 'ready')).
    .PARAMETER Failed
        Draw it as a failure (red cross) instead of a success.
    #>
    param(
        [string] $Title,
        [object[]] $Rows = @(),
        [string] $NextTitle,
        [string[]] $Next = @(),
        [string[]] $Notes = @(),
        [switch] $Failed
    )

    $markTone = 'accent'; $mark = $script:VxGlyph.Check
    if ($Failed) { $markTone = 'fail'; $mark = $script:VxGlyph.Cross }

    Write-Vx
    Write-Vx 'muted', ($script:VxIndent + ($script:VxGlyph.Rule * 46))
    Write-Vx
    Write-Vx $markTone, "$($script:VxIndent) $mark  ", 'ink', $Title

    $labelWidth = 0
    foreach ($row in $Rows) { if ("$($row[0])".Length -gt $labelWidth) { $labelWidth = "$($row[0])".Length } }
    if ($Rows.Count -gt 0) { Write-Vx }
    foreach ($row in $Rows) {
        Write-Vx 'muted', ("$($script:VxIndent)    " + "$($row[0])".PadRight($labelWidth + 3)), 'soft', "$($row[1])"
    }

    foreach ($note in $Notes) {
        Write-Vx
        foreach ($line in (Split-VxText $note ($script:VxIndent.Length + 4))) {
            Write-Vx 'soft', "$($script:VxIndent)    $line"
        }
    }

    if ($Next.Count -gt 0) {
        Write-Vx
        Write-Vx 'ink', "$($script:VxIndent)    $NextTitle"
        $n = 1
        foreach ($item in $Next) {
            Write-Vx 'accent', "$($script:VxIndent)    $n. ", 'soft', $item
            $n++
        }
    }
    Write-Vx
}

function Wait-VxClose {
    <#
    .SYNOPSIS
        Leaves the result on screen before the window closes.
    .PARAMETER Seconds
        With a number: counts down and returns (an update, where nobody is
        there to press a key). Without: waits for Enter.
    #>
    param([int] $Seconds = 0)

    if ($Seconds -le 0) {
        Write-Vx 'muted', "$($script:VxIndent)    $(Get-VxText 'PressEnter') " -NoNewline
        try { [void] (Read-Host) } catch { }
        return
    }

    if (-not $script:Vx.Live) {
        Write-Vx 'muted', "$($script:VxIndent)    $(Get-VxText 'ClosingIn' @($Seconds))"
        Start-Sleep -Seconds $Seconds
        return
    }

    for ($left = $Seconds; $left -gt 0; $left--) {
        $line = Format-VxSegments @('muted', "$($script:VxIndent)    $(Get-VxText 'ClosingIn' @($left))")
        try { [Console]::Out.Write("`r" + $line + "$($script:VxEsc)[K") } catch { }
        Start-Sleep -Seconds 1
    }
    Write-Vx
}
