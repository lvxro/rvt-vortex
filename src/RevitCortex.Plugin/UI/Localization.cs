using Autodesk.Revit.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Lightweight runtime localization. Detects Revit UI language on first use
/// and returns translated strings by key. Unknown keys or missing languages
/// fall back to English. Add new strings by extending <see cref="Table"/>.
/// </summary>
internal static class Localization
{
    private static string? _cachedLocale;

    /// <summary>
    /// Two-letter locale code ("en", "it", "fr", "de", "es"). Detected from the
    /// Windows display language first (what the user reads every day), then
    /// Revit's active <see cref="LanguageType"/>, then
    /// <see cref="CultureInfo.CurrentUICulture"/>. Unsupported languages fall
    /// back to English.
    /// </summary>
    public static string Locale
    {
        get
        {
            if (_cachedLocale != null) return _cachedLocale;
            _cachedLocale = DetectLocale();
            return _cachedLocale;
        }
    }

    /// <summary>Translate a key to the current Revit UI language.</summary>
    public static string T(string key)
    {
        if (Table.TryGetValue(key, out var variants))
        {
            if (variants.TryGetValue(Locale, out var s)) return s;
            if (variants.TryGetValue("en", out var en)) return en;
        }
        return key;
    }

    /// <summary>Translate and format ({0}, {1}, ...). If the translated
    /// string has mismatched placeholders (locale error), returns the raw
    /// template instead of throwing — the support flow must never crash on
    /// a bad translation.</summary>
    public static string T(string key, params object?[] args)
    {
        var fmt = T(key);
        try { return string.Format(fmt, args); }
        catch (FormatException) { return fmt; }
    }

    private static readonly string[] Supported = { "en", "it", "fr", "de", "es" };

    private static string DetectLocale()
    {
        try
        {
            var fromWindows = TryDetectFromWindows();
            if (fromWindows != null) return fromWindows;
        }
        catch { /* fall through */ }

        try
        {
            var fromRevit = TryDetectFromRevit();
            if (fromRevit != null) return fromRevit;
        }
        catch { /* fall through — RevitAPI unavailable (e.g. unit tests without Revit) */ }

        try
        {
            var iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant();
            if (iso == "it" || iso == "fr" || iso == "de" || iso == "es") return iso;
        }
        catch { /* ignore */ }

        return "en";
    }

    // Windows display language (Settings > Time & language > Language). Revit
    // may set the thread culture to its own install language, so
    // CurrentUICulture alone does not reflect what the user chose for Windows.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private static string? TryDetectFromWindows()
    {
        var lcid = GetUserDefaultUILanguage();
        if (lcid == 0) return null;
        var iso = new CultureInfo(lcid).TwoLetterISOLanguageName?.ToLowerInvariant();
        return iso != null && Array.IndexOf(Supported, iso) >= 0 ? iso : null;
    }

    // Separate method so the JIT only resolves RevitAPI (Autodesk.Revit.ApplicationServices)
    // when this is actually invoked (inside DetectLocale's try), not when DetectLocale itself
    // is JITted. Without this split, a bare reference to Application/LanguageType in
    // DetectLocale's body forces RevitAPI resolution to prepare DetectLocale at all, throwing
    // FileNotFoundException in unit tests that have no RevitAPI.dll in their output folder —
    // even when RevitCortexApp.Instance is null and the Revit-specific branch never executes.
    // Same pattern as RevitApiAvailability.ForceLoad in RequiresRevitApiFactAttribute.cs.
    private static string? TryDetectFromRevit()
    {
        var app = RevitCortexApp.Instance?.UiApplication?.Application;
        if (app == null) return null;

        return app.Language switch
        {
            LanguageType.Italian             => "it",
            LanguageType.French              => "fr",
            LanguageType.German              => "de",
            LanguageType.Spanish             => "es",
            LanguageType.English_USA         => "en",
            LanguageType.English_GB          => "en",
            _                                => "en"
        };
    }

    // Keys: dot-separated namespaces. Values: per-locale string.
    // Missing locale -> fallback to "en". Missing key -> return the key itself.
    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        // ── Support report ──────────────────────────────────────────────
        ["support.title"] = new()
        {
            ["en"] = "RVT Vortex",
            ["it"] = "RVT Vortex",
            ["es"] = "RVT Vortex",
        },
        ["support.already_running"] = new()
        {
            ["en"] = "A log report is already being generated. Please wait for the first one to finish before retrying.",
            ["it"] = "Invio log già in corso. Attendi il completamento del primo invio prima di riprovare.",
            ["es"] = "Ya se está generando un informe de registros. Espere a que termine antes de volver a intentar.",
        },
        ["support.outlook_opened"] = new()
        {
            ["en"] = "A draft email has been opened in Outlook with the attached file:\n\n{0}\n\nReview the content, add any notes, and click Send.",
            ["it"] = "Bozza email aperta in Outlook con il file allegato:\n\n{0}\n\nControlla il contenuto, aggiungi eventuali note e clicca Invia.",
            ["es"] = "Se abrió un borrador de correo en Outlook con el archivo adjunto:\n\n{0}\n\nRevise el contenido, agregue sus notas y haga clic en Enviar.",
        },
        ["support.outlook_unavailable"] = new()
        {
            ["en"] = "Outlook is not available or not responding. The diagnostic package has been created here:\n\n{0}\n\nPlease send it manually to {1} (email, Teams, OneDrive...).",
            ["it"] = "Outlook non disponibile o non risponde. Il pacchetto diagnostico è stato creato qui:\n\n{0}\n\nInvialo manualmente a {1} (email, Teams, OneDrive...).",
            ["es"] = "Outlook no está disponible o no responde. El paquete de diagnóstico se creó aquí:\n\n{0}\n\nEnvíelo manualmente a {1} (correo, Teams, OneDrive...).",
        },
        ["support.package_failed"] = new()
        {
            ["en"] = "Unable to create the diagnostic package: {0}",
            ["it"] = "Impossibile creare il pacchetto diagnostico: {0}",
            ["es"] = "No se pudo crear el paquete de diagnóstico: {0}",
        },

        // ── Support reports settings / cleanup ──────────────────────────
        ["support.settings.title"] = new()
        {
            ["en"] = "Support Reports",
            ["it"] = "Report di supporto",
            ["es"] = "Informes de soporte",
        },
        ["support.settings.subtitle"] = new()
        {
            ["en"] = "Number of reports to keep on disk",
            ["it"] = "Numero di report da conservare su disco",
            ["es"] = "Cantidad de informes a conservar en disco",
        },
        ["support.settings.delete_now"] = new()
        {
            ["en"] = "Delete all now",
            ["it"] = "Elimina tutti adesso",
            ["es"] = "Eliminar todos ahora",
        },
        ["support.settings.open_folder"] = new()
        {
            ["en"] = "Open folder",
            ["it"] = "Apri cartella",
            ["es"] = "Abrir carpeta",
        },
        ["support.settings.open_folder_failed"] = new()
        {
            ["en"] = "Unable to open the folder: {0}",
            ["it"] = "Impossibile aprire la cartella: {0}",
            ["es"] = "No se pudo abrir la carpeta: {0}",
        },

        // ── Update checker ──────────────────────────────────────────────
        ["update.available_title"] = new()
        {
            ["en"] = "RVT Vortex {0} is available",
            ["it"] = "È disponibile RVT Vortex {0}",
            ["es"] = "Está disponible RVT Vortex {0}",
        },
        ["update.available_detail"] = new()
        {
            ["en"] = "You are on {0}. Click Download to get the new release.",
            ["it"] = "Versione installata: {0}. Clicca Download per scaricare la nuova release.",
            ["es"] = "Versión instalada: {0}. Haga clic en Descargar para obtener la nueva versión.",
        },
        ["update.download_button"] = new()
        {
            ["en"] = "Download",
            ["it"] = "Scarica",
            ["es"] = "Descargar",
        },
        ["update.open_browser_failed"] = new()
        {
            ["en"] = "Unable to open the download link: {0}",
            ["it"] = "Impossibile aprire il link di download: {0}",
            ["es"] = "No se pudo abrir el enlace de descarga: {0}",
        },
        ["support.cleanup.confirm_title"] = new()
        {
            ["en"] = "Delete all support reports?",
            ["it"] = "Eliminare tutti i report di supporto?",
            ["es"] = "¿Eliminar todos los informes de soporte?",
        },
        ["support.cleanup.confirm_body"] = new()
        {
            ["en"] = "Found {0} report(s) ({1}). All files in the folder will be permanently deleted. Continue?",
            ["it"] = "Trovati {0} report ({1}). Tutti i file nella cartella verranno eliminati in modo permanente. Continuare?",
            ["es"] = "Se encontraron {0} informe(s) ({1}). Todos los archivos de la carpeta se eliminarán de forma permanente. ¿Continuar?",
        },
        ["support.cleanup.none"] = new()
        {
            ["en"] = "No support reports to delete.",
            ["it"] = "Nessun report di supporto da eliminare.",
            ["es"] = "No hay informes de soporte para eliminar.",
        },
        ["support.cleanup.done"] = new()
        {
            ["en"] = "{0} report(s) deleted.",
            ["it"] = "{0} report eliminati.",
            ["es"] = "{0} informe(s) eliminado(s).",
        },
        ["support.cleanup.partial"] = new()
        {
            ["en"] = "{0} report(s) deleted. {1} could not be removed (files in use).",
            ["it"] = "{0} report eliminati. {1} non rimossi (file in uso).",
            ["es"] = "{0} informe(s) eliminado(s). {1} no se pudieron quitar (archivos en uso).",
        },

        // ── Telemetry consent ───────────────────────────────────────────
        ["telemetry.consent_instruction"] = new()
        {
            ["en"] = "Do you consent to sending anonymous error reports to help us improve the product?",
            ["it"] = "Acconsenti a inviare segnalazioni di errore anonime per aiutarci a migliorare il prodotto?",
            ["es"] = "¿Acepta enviar informes de error anónimos para ayudar a mejorar el producto?",
        },
        ["telemetry.consent_body"] = new()
        {
            ["en"] = "When a command fails, RVT Vortex can send an anonymous error report to the developers of the original RevitCortex project: tool name, error type, versions, timing. Never sent: model names, file paths, parameter values, user or machine names.\n\nConsent is optional and you can change it anytime in Settings > General.",
            ["it"] = "Quando un comando fallisce, RVT Vortex può inviare una segnalazione di errore anonima agli sviluppatori del progetto originale RevitCortex: nome del tool, tipo di errore, versioni, tempi. Mai inviati: nomi dei modelli, percorsi file, valori dei parametri, nomi utente o macchina.\n\nIl consenso è facoltativo e puoi modificarlo in qualsiasi momento da Impostazioni > Generale.",
            ["es"] = "Cuando un comando falla, RVT Vortex puede enviar un informe de error anónimo a los desarrolladores del proyecto original RevitCortex: nombre de la herramienta, tipo de error, versiones y tiempos. Nunca se envían nombres de modelos, rutas de archivos, valores de parámetros ni nombres de usuario o equipo.\n\nEl consentimiento es opcional y puede cambiarlo cuando quiera en Configuración > General.",
        },
        ["telemetry.consent_enable"] = new()
        {
            ["en"] = "Enable error telemetry",
            ["it"] = "Attiva la telemetria errori",
            ["es"] = "Activar telemetría de errores",
        },
        ["telemetry.consent_decline"] = new()
        {
            ["en"] = "Keep it disabled",
            ["it"] = "Lascia disattivata",
            ["es"] = "Dejarla desactivada",
        },
        ["telemetry.settings_toggle"] = new()
        {
            ["en"] = "Send anonymous error telemetry (no model data)",
            ["it"] = "Invia telemetria errori anonima (nessun dato del modello)",
            ["es"] = "Enviar telemetría de errores anónima (sin datos del modelo)",
        },
        // ── License & Account ───────────────────────────────────────────
        ["license.window_title"] = new()
        {
            ["en"] = "License & Account",
            ["it"] = "Licenza e account",
            ["es"] = "Licencia y cuenta",
        },
        ["license.header_subtitle"] = new()
        {
            ["en"] = "View license status and activate RevitCortex Premium",
            ["it"] = "Visualizza lo stato della licenza e attiva RevitCortex Premium",
            ["es"] = "Ver el estado de la licencia y activar RevitCortex Premium",
        },
        ["license.banner_detail_active"] = new()
        {
            ["en"] = "Your license is active — all commands are available",
            ["it"] = "La licenza è attiva — tutti i comandi sono disponibili",
            ["es"] = "Su licencia está activa — todos los comandos están disponibles",
        },
        ["license.banner_detail_trial"] = new()
        {
            ["en"] = "You're on a trial license — all commands are available",
            ["it"] = "Licenza di prova in corso — tutti i comandi sono disponibili",
            ["es"] = "Está usando una licencia de prueba — todos los comandos están disponibles",
        },
        ["license.banner_detail_grace"] = new()
        {
            ["en"] = "Offline grace period — reconnect soon to keep write commands enabled",
            ["it"] = "Periodo di tolleranza offline — riconnettiti presto per mantenere attivi i comandi di scrittura",
            ["es"] = "Período de gracia sin conexión — vuelva a conectarse pronto para mantener los comandos de edición",
        },
        ["license.banner_detail_expired"] = new()
        {
            ["en"] = "License expired — write commands are blocked, read-only still works",
            ["it"] = "Licenza scaduta — i comandi di scrittura sono bloccati, la sola lettura funziona ancora",
            ["es"] = "Licencia vencida — los comandos de edición están bloqueados; la lectura sigue funcionando",
        },
        ["license.banner_detail_invalid"] = new()
        {
            ["en"] = "No valid license found — activate a key below to unlock write commands",
            ["it"] = "Nessuna licenza valida trovata — attiva una chiave qui sotto per sbloccare i comandi di scrittura",
            ["es"] = "No se encontró una licencia válida — active una clave abajo para habilitar los comandos de edición",
        },
        ["license.state_label"] = new()
        {
            ["en"] = "Status:",
            ["it"] = "Stato:",
            ["es"] = "Estado:",
        },
        ["license.state_active"] = new()
        {
            ["en"] = "Active",
            ["it"] = "Attiva",
            ["es"] = "Activa",
        },
        ["license.state_trial"] = new()
        {
            ["en"] = "Trial",
            ["it"] = "Prova",
            ["es"] = "Prueba",
        },
        ["license.state_grace"] = new()
        {
            ["en"] = "Offline (grace)",
            ["it"] = "Offline (periodo di tolleranza)",
            ["es"] = "Sin conexión (período de gracia)",
        },
        ["license.state_expired"] = new()
        {
            ["en"] = "Expired",
            ["it"] = "Scaduta",
            ["es"] = "Vencida",
        },
        ["license.state_invalid"] = new()
        {
            ["en"] = "Not activated / invalid",
            ["it"] = "Non attivata / non valida",
            ["es"] = "No activada / no válida",
        },
        ["license.expiry_label"] = new()
        {
            ["en"] = "Expires:",
            ["it"] = "Scadenza:",
            ["es"] = "Vence:",
        },
        ["license.grace_label"] = new()
        {
            ["en"] = "Offline days remaining:",
            ["it"] = "Giorni offline rimanenti:",
            ["es"] = "Días sin conexión restantes:",
        },
        ["license.id_label"] = new()
        {
            ["en"] = "License ID:",
            ["it"] = "ID licenza:",
            ["es"] = "ID de licencia:",
        },
        ["license.key_label"] = new()
        {
            ["en"] = "License key:",
            ["it"] = "Chiave di licenza:",
            ["es"] = "Clave de licencia:",
        },
        ["license.activate_button"] = new()
        {
            ["en"] = "Activate",
            ["it"] = "Attiva",
            ["es"] = "Activar",
        },
        ["license.refresh_button"] = new()
        {
            ["en"] = "Refresh",
            ["it"] = "Aggiorna",
            ["es"] = "Actualizar",
        },
        ["license.activate_ok"] = new()
        {
            ["en"] = "License activated. Status: {0}.",
            ["it"] = "Licenza attivata. Stato: {0}.",
            ["es"] = "Licencia activada. Estado: {0}.",
        },
        ["license.activate_failed"] = new()
        {
            ["en"] = "Activation failed: {0}",
            ["it"] = "Attivazione non riuscita: {0}",
            ["es"] = "La activación falló: {0}",
        },
        ["license.dev_transparent"] = new()
        {
            ["en"] = "Dev profile — licensing is transparent (always active).",
            ["it"] = "Profilo dev — licenza trasparente (sempre attiva).",
            ["es"] = "Perfil de desarrollo — la licencia es transparente (siempre activa).",
        },
        ["license.expired_hint"] = new()
        {
            ["en"] = "Write commands are blocked until you renew. Read-only commands still work.",
            ["it"] = "I comandi di scrittura sono bloccati fino al rinnovo. I comandi di sola lettura restano attivi.",
            ["es"] = "Los comandos de edición están bloqueados hasta que renueve. Los comandos de solo lectura siguen funcionando.",
        },
        ["license.gate_blocked"] = new()
        {
            ["en"] = "License not active: RevitCortex Premium is running in read-only mode. Editing command '{0}' is disabled until you activate a valid license.",
            ["it"] = "Licenza non attiva: RevitCortex Premium funziona in sola lettura. Il comando di modifica '{0}' è disattivato finché non attivi una licenza valida.",
            ["es"] = "Licencia no activa: RevitCortex Premium funciona en modo solo lectura. El comando de edición '{0}' está deshabilitado hasta que active una licencia válida.",
        },
        ["license.gate_suggestion"] = new()
        {
            ["en"] = "Activate a license in RevitCortex > License & Account. Read-only commands remain available.",
            ["it"] = "Attiva una licenza da RevitCortex > Licenza e account. I comandi di sola lettura restano disponibili.",
            ["es"] = "Active una licencia en RevitCortex > Licencia y cuenta. Los comandos de solo lectura siguen disponibles.",
        },

        ["support.issue_opened"] = new()
        {
            ["en"] = "The diagnostic package was created here:\n\n{0}\n\nA new issue page has opened in your browser ({1}). Describe the problem and drag the ZIP into it.\n\nThe ZIP leaves out your user name, machine name, model names, file paths, tool inputs and the Revit journal. Issues are public, so still take a look inside before attaching it.",
            ["es"] = "El paquete de diagnóstico se creó aquí:\n\n{0}\n\nSe abrió en el navegador la página para crear un reporte ({1}). Describa el problema y arrastre el ZIP.\n\nEl ZIP no incluye su nombre de usuario, el nombre del equipo, nombres de modelos, rutas de archivos, los datos enviados a las herramientas ni el journal de Revit. Los reportes son públicos, así que igual conviene revisarlo antes de adjuntarlo.",
            ["it"] = "Il pacchetto diagnostico è stato creato qui:\n\n{0}\n\nNel browser si è aperta la pagina per segnalare il problema ({1}). Descrivi il problema e trascina lo ZIP.\n\nLo ZIP non contiene il tuo nome utente, il nome del computer, i nomi dei modelli, i percorsi dei file, i dati passati agli strumenti né il journal di Revit. Le segnalazioni sono pubbliche: conviene comunque controllarlo prima di allegarlo.",
        },
        ["support.private_created"] = new()
        {
            ["en"] = "The FULL diagnostic package was created here:\n\n{0}\n\nIt includes private data (user and machine name, model names and paths, tool inputs, the Revit journal) because \"SupportReportIncludePrivateData\" is on in settings.json. Do not attach it to a public issue: send it privately.",
            ["es"] = "El paquete de diagnóstico COMPLETO se creó aquí:\n\n{0}\n\nIncluye datos privados (usuario y equipo, nombres y rutas de modelos, datos enviados a las herramientas, el journal de Revit) porque \"SupportReportIncludePrivateData\" está activado en settings.json. No lo adjunte a un reporte público: envíelo en privado.",
            ["it"] = "Il pacchetto diagnostico COMPLETO è stato creato qui:\n\n{0}\n\nContiene dati privati (utente e computer, nomi e percorsi dei modelli, dati passati agli strumenti, il journal di Revit) perché \"SupportReportIncludePrivateData\" è attivo in settings.json. Non allegarlo a una segnalazione pubblica: invialo in privato.",
        },
        // ── Fork: ribbon, dialogs and Autopilot (en / es / it) ─────────
        ["ribbon.connect.tooltip_off"] = new()
        {
            ["en"] = "Start the RVT Vortex server so the AI can work in this model",
            ["es"] = "Iniciar el servidor de RVT Vortex para que la IA pueda trabajar en este modelo",
            ["it"] = "Avvia il server RVT Vortex perché l'IA possa lavorare su questo modello",
        },
        ["ribbon.connect.tooltip_on"] = new()
        {
            ["en"] = "RVT Vortex server running on port {0} — click to stop",
            ["es"] = "Servidor de RVT Vortex activo en el puerto {0} — clic para detenerlo",
            ["it"] = "Server RVT Vortex attivo sulla porta {0} — clic per fermarlo",
        },
        ["ribbon.autopilot.text_off"] = new()
        {
            ["en"] = "Autopilot",
            ["es"] = "Piloto\r\nautomático",
            ["it"] = "Pilota\r\nautomatico",
        },
        ["ribbon.autopilot.text_on"] = new()
        {
            ["en"] = "Autopilot\r\nON",
            ["es"] = "Piloto\r\nACTIVO",
            ["it"] = "Pilota\r\nATTIVO",
        },
        ["ribbon.autopilot.tooltip_off"] = new()
        {
            ["en"] = "Autopilot: let the AI keep working while you are away",
            ["es"] = "Piloto automático: la IA sigue trabajando mientras usted no está",
            ["it"] = "Pilota automatico: l'IA continua a lavorare mentre sei via",
        },
        ["ribbon.autopilot.tooltip_on"] = new()
        {
            ["en"] = "Autopilot is ON — click to turn it off",
            ["es"] = "Piloto automático ACTIVO — clic para apagarlo",
            ["it"] = "Pilota automatico ATTIVO — clic per spegnerlo",
        },
        ["ribbon.autopilot.long"] = new()
        {
            ["en"] = "Approves ordinary edits without dialogs, declines critical ones instead of waiting, closes Revit pop-ups, saves the model after changes and logs everything to autopilot.log.",
            ["es"] = "Aprueba las modificaciones normales sin cuadros de diálogo, rechaza las críticas en vez de esperar, cierra los avisos de Revit, guarda el modelo después de los cambios y anota todo en autopilot.log.",
            ["it"] = "Approva le modifiche ordinarie senza finestre di dialogo, rifiuta quelle critiche invece di attendere, chiude gli avvisi di Revit, salva il modello dopo le modifiche e registra tutto in autopilot.log.",
        },
        ["ribbon.settings.text"] = new()
        {
            ["en"] = "Settings",
            ["es"] = "Configuración",
            ["it"] = "Impostazioni",
        },
        ["ribbon.settings.tooltip"] = new()
        {
            ["en"] = "RVT Vortex settings",
            ["es"] = "Configuración de RVT Vortex",
            ["it"] = "Impostazioni di RVT Vortex",
        },
        ["ribbon.powerbi.text"] = new()
        {
            ["en"] = "Power BI\r\nExport",
            ["es"] = "Exportar\r\nPower BI",
            ["it"] = "Esporta\r\nPower BI",
        },
        ["ribbon.powerbi.tooltip"] = new()
        {
            ["en"] = "Export data and parameters to CSV for Power BI",
            ["es"] = "Exportar datos y parámetros a CSV para Power BI",
            ["it"] = "Esporta dati e parametri in CSV per Power BI",
        },
        ["ribbon.powerbi.long"] = new()
        {
            ["en"] = "Opens the Power BI export wizard: choose categories and parameters, save reusable profiles, enable auto-export on save and register the revitcortex:// protocol handler for drill-through from Power BI to Revit.",
            ["es"] = "Abre el asistente de exportación a Power BI: elija categorías y parámetros, guarde perfiles reutilizables, active la exportación automática al guardar y registre el protocolo revitcortex:// para navegar de Power BI a Revit.",
            ["it"] = "Apre il wizard di export Power BI: scegli categorie e parametri, salva profili riutilizzabili, abilita auto-export al salvataggio e registra il protocol handler revitcortex:// per drillthrough da PBI a Revit.",
        },
        ["ribbon.support.text"] = new()
        {
            ["en"] = "Report\r\na bug",
            ["es"] = "Reportar\r\nerror",
            ["it"] = "Segnala\r\nproblema",
        },
        ["ribbon.support.tooltip"] = new()
        {
            ["en"] = "Report a bug on the RVT Vortex GitHub page",
            ["es"] = "Reportar un error en la página de GitHub de RVT Vortex",
            ["it"] = "Segnala un problema sulla pagina GitHub di RVT Vortex",
        },
        ["ribbon.support.long"] = new()
        {
            ["en"] = "Packs a redacted copy of the recent audit log and the settings into a ZIP (no user or machine name, model names, paths, tool inputs or Revit journal), shows it in Explorer and opens a new issue on the RVT Vortex GitHub page. Issues are public: check the ZIP before attaching it.",
            ["es"] = "Reúne en un ZIP una copia depurada del registro de auditoría reciente y de la configuración (sin usuario ni equipo, nombres de modelos, rutas, datos enviados a las herramientas ni el journal de Revit), lo muestra en el Explorador y abre un reporte nuevo en la página de GitHub de RVT Vortex. Los reportes son públicos: revise el ZIP antes de adjuntarlo.",
            ["it"] = "Raccoglie in uno ZIP una copia ripulita del log di audit recente e delle impostazioni (senza utente né computer, nomi dei modelli, percorsi, dati passati agli strumenti o journal di Revit), lo mostra in Esplora risorse e apre una nuova segnalazione sulla pagina GitHub di RVT Vortex. Le segnalazioni sono pubbliche: controlla lo ZIP prima di allegarlo.",
        },
        ["ribbon.license.text"] = new()
        {
            ["en"] = "License &\r\nAccount",
            ["es"] = "Licencia\r\ny cuenta",
            ["it"] = "Licenza e\r\naccount",
        },
        ["ribbon.license.tooltip"] = new()
        {
            ["en"] = "View license status and activate RevitCortex Premium",
            ["es"] = "Ver el estado de la licencia y activar RevitCortex Premium",
            ["it"] = "Visualizza lo stato della licenza e attiva RevitCortex Premium",
        },
        ["conn.not_initialized"] = new()
        {
            ["en"] = "Plugin not initialized.",
            ["es"] = "El complemento no está inicializado.",
            ["it"] = "Plugin non inizializzato.",
        },
        ["conn.not_initialized_detail"] = new()
        {
            ["en"] = "RVT Vortex failed while starting, so this button can't run. Expand the details below for the error; it was also saved to:\n{0}\n\nPlease include it when you report the bug.",
            ["es"] = "RVT Vortex falló al iniciar, por eso este botón no puede funcionar. Despliegue los detalles de abajo para ver el error; también se guardó en:\n{0}\n\nInclúyalo al reportar el problema.",
            ["it"] = "RVT Vortex non si è avviato correttamente, quindi questo pulsante non può funzionare. Espandi i dettagli qui sotto per l'errore; è stato salvato anche in:\n{0}\n\nIncludilo quando segnali il problema.",
        },
        ["conn.not_initialized_unknown"] = new()
        {
            ["en"] = "RVT Vortex did not finish starting and no error was recorded. Restart Revit; if it happens again, report it with the latest Revit journal.",
            ["es"] = "RVT Vortex no terminó de iniciar y no quedó registrado ningún error. Reinicie Revit; si vuelve a pasar, repórtelo junto con el último journal de Revit.",
            ["it"] = "RVT Vortex non ha completato l'avvio e non è stato registrato alcun errore. Riavvia Revit; se succede di nuovo, segnalalo con l'ultimo journal di Revit.",
        },
        ["conn.stopped"] = new()
        {
            ["en"] = "Server stopped.",
            ["es"] = "Servidor detenido.",
            ["it"] = "Server fermato.",
        },
        ["conn.started"] = new()
        {
            ["en"] = "Server started on port {0}.",
            ["es"] = "Servidor iniciado en el puerto {0}.",
            ["it"] = "Server avviato sulla porta {0}.",
        },
        ["confirm.title"] = new()
        {
            ["en"] = "RVT Vortex — Confirmation",
            ["es"] = "RVT Vortex — Confirmación",
            ["it"] = "RVT Vortex — Conferma",
        },
        ["confirm.critical_title"] = new()
        {
            ["en"] = "RVT Vortex — Critical confirmation",
            ["es"] = "RVT Vortex — Confirmación crítica",
            ["it"] = "RVT Vortex — Conferma critica",
        },
        ["confirm.instruction"] = new()
        {
            ["en"] = "About to {0} ({1} element(s))",
            ["es"] = "Se va a ejecutar: {0} ({1} elemento(s))",
            ["it"] = "Operazione: {0} ({1} elemento/i)",
        },
        ["confirm.yes"] = new()
        {
            ["en"] = "Yes",
            ["es"] = "Sí",
            ["it"] = "Sì",
        },
        ["confirm.yes_desc"] = new()
        {
            ["en"] = "Approve this operation",
            ["es"] = "Aprobar esta operación",
            ["it"] = "Approva questa operazione",
        },
        ["confirm.yes_only_desc"] = new()
        {
            ["en"] = "Approve this operation only",
            ["es"] = "Aprobar solo esta operación",
            ["it"] = "Approva solo questa operazione",
        },
        ["confirm.yes_all"] = new()
        {
            ["en"] = "Yes to All",
            ["es"] = "Sí a todo",
            ["it"] = "Sì a tutto",
        },
        ["confirm.yes_all_desc"] = new()
        {
            ["en"] = "Approve this and all remaining operations without asking again (2 min)",
            ["es"] = "Aprobar esta y las siguientes operaciones sin volver a preguntar (2 min)",
            ["it"] = "Approva questa e le successive operazioni senza chiedere di nuovo (2 min)",
        },
        ["confirm.auto"] = new()
        {
            ["en"] = "Auto",
            ["es"] = "Automático",
            ["it"] = "Automatico",
        },
        ["confirm.auto_desc"] = new()
        {
            ["en"] = "Approve all operations automatically — a floating window lets you stop at any time",
            ["es"] = "Aprobar todo automáticamente — una ventana flotante permite detenerlo en cualquier momento",
            ["it"] = "Approva tutto automaticamente — una finestra mobile permette di fermarsi in qualsiasi momento",
        },
        ["confirm.no"] = new()
        {
            ["en"] = "No",
            ["es"] = "No",
            ["it"] = "No",
        },
        ["confirm.no_desc"] = new()
        {
            ["en"] = "Cancel this operation",
            ["es"] = "Cancelar esta operación",
            ["it"] = "Annulla questa operazione",
        },
        ["ap.title"] = new()
        {
            ["en"] = "RVT Vortex — Autopilot",
            ["es"] = "RVT Vortex — Piloto automático",
            ["it"] = "RVT Vortex — Pilota automatico",
        },
        ["ap.instruction"] = new()
        {
            ["en"] = "Turn on Autopilot?",
            ["es"] = "¿Activar el piloto automático?",
            ["it"] = "Attivare il pilota automatico?",
        },
        ["ap.content"] = new()
        {
            ["en"] = "While it is on:\n• Ordinary edits are approved without confirmation dialogs.\n• C# scripts (send_code_to_revit) are DECLINED instead of waiting for you, unless you tick the box below.\n• Revit pop-ups are closed automatically, choosing Cancel/Close when possible.\n• The model is saved after changes (at most once a minute); Revit keeps a backup copy on each save.\n• Everything is logged to:\n  {0}\n\nTip: save a copy of the model before you leave.",
            ["es"] = "Mientras esté activo:\n• Las modificaciones normales se aprueban sin cuadros de confirmación.\n• Los scripts C# (send_code_to_revit) se RECHAZAN en vez de esperar, salvo que marque la casilla de abajo.\n• Los avisos de Revit se cierran solos, eligiendo Cancelar/Cerrar cuando se puede.\n• El modelo se guarda después de los cambios (como mucho una vez por minuto) y Revit deja una copia de respaldo en cada guardado.\n• Todo queda anotado en:\n  {0}\n\nConsejo: guarde una copia del modelo antes de irse.",
            ["it"] = "Mentre è attivo:\n• Le modifiche ordinarie vengono approvate senza finestre di conferma.\n• Gli script C# (send_code_to_revit) vengono RIFIUTATI invece di attendere, a meno che non spunti la casella qui sotto.\n• Gli avvisi di Revit vengono chiusi automaticamente, scegliendo Annulla/Chiudi quando possibile.\n• Il modello viene salvato dopo le modifiche (al massimo una volta al minuto); Revit conserva una copia di backup a ogni salvataggio.\n• Tutto viene registrato in:\n  {0}\n\nConsiglio: salva una copia del modello prima di allontanarti.",
        },
        ["ap.verification"] = new()
        {
            ["en"] = "Also allow C# scripts (send_code_to_revit) without asking",
            ["es"] = "Permitir también scripts C# (send_code_to_revit) sin preguntar",
            ["it"] = "Consenti anche gli script C# (send_code_to_revit) senza chiedere",
        },
        ["ap.footer_server_off"] = new()
        {
            ["en"] = "The server (Vortex Switch) is off. Turn it on so the AI can work.",
            ["es"] = "El servidor (Vortex Switch) está apagado. Enciéndalo para que la IA pueda trabajar.",
            ["it"] = "Il server (Vortex Switch) è spento. Accendilo perché l'IA possa lavorare.",
        },
        ["ap.footer_unsaved"] = new()
        {
            ["en"] = "This model has never been saved, so it cannot be saved automatically. Save it once before you leave.",
            ["es"] = "Este modelo nunca se guardó, así que no se puede guardar automáticamente. Guárdelo una vez antes de irse.",
            ["it"] = "Questo modello non è mai stato salvato, quindi non può essere salvato automaticamente. Salvalo una volta prima di allontanarti.",
        },
        ["win.title"] = new()
        {
            ["en"] = "RVT Vortex",
            ["es"] = "RVT Vortex",
            ["it"] = "RVT Vortex",
        },
        ["win.auto_heading"] = new()
        {
            ["en"] = "Auto mode",
            ["es"] = "Modo automático",
            ["it"] = "Modalità automatica",
        },
        ["win.auto_detail"] = new()
        {
            ["en"] = "Edits are approved without confirmation dialogs.",
            ["es"] = "Las modificaciones se aprueban sin cuadros de confirmación.",
            ["it"] = "Le modifiche vengono approvate senza finestre di conferma.",
        },
        ["win.autopilot_heading"] = new()
        {
            ["en"] = "Autopilot",
            ["es"] = "Piloto automático",
            ["it"] = "Pilota automatico",
        },
        ["win.autopilot_detail"] = new()
        {
            ["en"] = "Edits are approved without dialogs, Revit pop-ups are closed, and the model is saved after changes. Everything is logged to autopilot.log.",
            ["es"] = "Las modificaciones se aprueban sin cuadros, los avisos de Revit se cierran solos y el modelo se guarda después de los cambios. Todo queda en autopilot.log.",
            ["it"] = "Le modifiche vengono approvate senza finestre, gli avvisi di Revit si chiudono da soli e il modello viene salvato dopo le modifiche. Tutto finisce in autopilot.log.",
        },
        ["win.stop"] = new()
        {
            ["en"] = "Stop",
            ["es"] = "Detener",
            ["it"] = "Ferma",
        },
        ["win.stop_desc"] = new()
        {
            ["en"] = "Turn off and bring back confirmation dialogs",
            ["es"] = "Apagar y volver a mostrar los cuadros de confirmación",
            ["it"] = "Spegni e ripristina le finestre di conferma",
        },
        ["win.status_idle"] = new()
        {
            ["en"] = "Idle",
            ["es"] = "En espera",
            ["it"] = "In attesa",
        },
        ["win.status_approved"] = new()
        {
            ["en"] = "{0}  ✓ {1} ({2})",
            ["es"] = "{0}  ✓ {1} ({2})",
            ["it"] = "{0}  ✓ {1} ({2})",
        },
        ["win.status_declined"] = new()
        {
            ["en"] = "{0}  ✕ {1} ({2})",
            ["es"] = "{0}  ✕ {1} ({2})",
            ["it"] = "{0}  ✕ {1} ({2})",
        },
        ["win.status_saved"] = new()
        {
            ["en"] = "{0}  Model saved",
            ["es"] = "{0}  Modelo guardado",
            ["it"] = "{0}  Modello salvato",
        },
        ["log.start"] = new()
        {
            ["en"] = "AUTOPILOT ON",
            ["es"] = "PILOTO AUTOMÁTICO ACTIVADO",
            ["it"] = "PILOTA AUTOMATICO ATTIVATO",
        },
        ["log.start_scripts"] = new()
        {
            ["en"] = "AUTOPILOT ON (C# scripts allowed)",
            ["es"] = "PILOTO AUTOMÁTICO ACTIVADO (scripts C# permitidos)",
            ["it"] = "PILOTA AUTOMATICO ATTIVATO (script C# consentiti)",
        },
        ["log.stop"] = new()
        {
            ["en"] = "AUTOPILOT OFF",
            ["es"] = "PILOTO AUTOMÁTICO DETENIDO",
            ["it"] = "PILOTA AUTOMATICO FERMATO",
        },
        ["log.stop_doc_closed"] = new()
        {
            ["en"] = "AUTOPILOT OFF (document closed)",
            ["es"] = "PILOTO AUTOMÁTICO DETENIDO (se cerró el documento)",
            ["it"] = "PILOTA AUTOMATICO FERMATO (documento chiuso)",
        },
        ["log.approved"] = new()
        {
            ["en"] = "APPROVED automatically: {0} ({1} element(s)){2}",
            ["es"] = "APROBADO automáticamente: {0} ({1} elemento(s)){2}",
            ["it"] = "APPROVATO automaticamente: {0} ({1} elemento/i){2}",
        },
        ["log.declined"] = new()
        {
            ["en"] = "DECLINED (needs your confirmation): {0} ({1} element(s)){2}",
            ["es"] = "RECHAZADO (necesita su confirmación): {0} ({1} elemento(s)){2}",
            ["it"] = "RIFIUTATO (serve la tua conferma): {0} ({1} elemento/i){2}",
        },
        ["log.dialog_closed"] = new()
        {
            ["en"] = "REVIT DIALOG closed ({0}, id '{1}', answer: {2}): {3}",
            ["es"] = "AVISO DE REVIT cerrado ({0}, id '{1}', respuesta: {2}): {3}",
            ["it"] = "AVVISO DI REVIT chiuso ({0}, id '{1}', risposta: {2}): {3}",
        },
        ["log.dialog_failed"] = new()
        {
            ["en"] = "REVIT DIALOG could NOT be closed ({0}, id '{1}'). Revit may be waiting for a click: {2}",
            ["es"] = "AVISO DE REVIT que NO se pudo cerrar ({0}, id '{1}'). Revit puede estar esperando un clic: {2}",
            ["it"] = "AVVISO DI REVIT NON chiuso ({0}, id '{1}'). Revit potrebbe attendere un clic: {2}",
        },
        ["log.answer_ok"] = new()
        {
            ["en"] = "OK",
            ["es"] = "Aceptar",
            ["it"] = "OK",
        },
        ["log.answer_cancel"] = new()
        {
            ["en"] = "Cancel",
            ["es"] = "Cancelar",
            ["it"] = "Annulla",
        },
        ["log.answer_no"] = new()
        {
            ["en"] = "No",
            ["es"] = "No",
            ["it"] = "No",
        },
        ["log.answer_close"] = new()
        {
            ["en"] = "Close",
            ["es"] = "Cerrar",
            ["it"] = "Chiudi",
        },
        ["log.saved"] = new()
        {
            ["en"] = "SAVED ({0}) after: {1}",
            ["es"] = "GUARDADO ({0}) después de: {1}",
            ["it"] = "SALVATO ({0}) dopo: {1}",
        },
        ["log.not_saved_readonly"] = new()
        {
            ["en"] = "NOT SAVED: the model is open as read-only.",
            ["es"] = "NO SE GUARDÓ: el modelo está abierto como solo lectura.",
            ["it"] = "NON SALVATO: il modello è aperto in sola lettura.",
        },
        ["log.not_saved_never"] = new()
        {
            ["en"] = "NOT SAVED: the model has never been saved (no file). Save it once before leaving.",
            ["es"] = "NO SE GUARDÓ: el modelo nunca se guardó (no tiene archivo). Guárdelo una vez antes de irse.",
            ["it"] = "NON SALVATO: il modello non è mai stato salvato (nessun file). Salvalo una volta prima di allontanarti.",
        },
        ["log.save_error"] = new()
        {
            ["en"] = "SAVE ERROR after: {0} — {1}",
            ["es"] = "ERROR AL GUARDAR después de: {0} — {1}",
            ["it"] = "ERRORE DI SALVATAGGIO dopo: {0} — {1}",
        },
        ["pbi.open_model_first"] = new()
        {
            ["en"] = "Open a Revit model first.",
            ["es"] = "Primero abra un modelo de Revit.",
            ["it"] = "Apri prima un modello Revit.",
        },
        ["pbi.error_title"] = new()
        {
            ["en"] = "Power BI Export — error",
            ["es"] = "Exportar Power BI — error",
            ["it"] = "Power BI Export — errore",
        },
        // ── Fork: update notifications (en / es / it) ────────────────
        ["upd.window_title"] = new()
        {
            ["en"] = "RVT Vortex — Update",
            ["es"] = "RVT Vortex — Actualización",
            ["it"] = "RVT Vortex — Aggiornamento",
        },
        ["upd.available"] = new()
        {
            ["en"] = "RVT Vortex {0} is available",
            ["es"] = "Está disponible RVT Vortex {0}",
            ["it"] = "È disponibile RVT Vortex {0}",
        },
        ["upd.current"] = new()
        {
            ["en"] = "Installed version: {0}",
            ["es"] = "Versión instalada: {0}",
            ["it"] = "Versione installata: {0}",
        },
        ["upd.current_with_notes"] = new()
        {
            ["en"] = "Installed: {0} — {1}",
            ["es"] = "Instalada: {0} — {1}",
            ["it"] = "Installata: {0} — {1}",
        },
        ["upd.update_now"] = new()
        {
            ["en"] = "Update now",
            ["es"] = "Actualizar ahora",
            ["it"] = "Aggiorna ora",
        },
        ["upd.later"] = new()
        {
            ["en"] = "Later",
            ["es"] = "Más tarde",
            ["it"] = "Più tardi",
        },
        ["upd.cancel"] = new()
        {
            ["en"] = "Cancel",
            ["es"] = "Cancelar",
            ["it"] = "Annulla",
        },
        ["upd.downloaded_mb"] = new()
        {
            ["en"] = "{0} MB downloaded…",
            ["es"] = "{0} MB descargados…",
            ["it"] = "{0} MB scaricati…",
        },
        ["upd.downloading"] = new()
        {
            ["en"] = "Downloading… {0}",
            ["es"] = "Descargando… {0}",
            ["it"] = "Download in corso… {0}",
        },
        ["upd.ready"] = new()
        {
            ["en"] = "Ready to install",
            ["es"] = "Listo para instalar",
            ["it"] = "Pronto per l'installazione",
        },
        ["upd.ready_detail"] = new()
        {
            ["en"] = "Read the note below before continuing.",
            ["es"] = "Lea el aviso de abajo antes de continuar.",
            ["it"] = "Leggi l'avviso qui sotto prima di continuare.",
        },
        ["upd.confirm_note"] = new()
        {
            ["en"] = "Revit will close to finish the installation. Save your work before continuing.",
            ["es"] = "Revit se va a cerrar para terminar la instalación. Guarde su trabajo antes de continuar.",
            ["it"] = "Revit verrà chiuso per completare l'installazione. Salva il lavoro prima di continuare.",
        },
        ["upd.install_close"] = new()
        {
            ["en"] = "Install now and close Revit",
            ["es"] = "Instalar ahora y cerrar Revit",
            ["it"] = "Installa ora e chiudi Revit",
        },
        ["upd.installing"] = new()
        {
            ["en"] = "Installation started",
            ["es"] = "Instalación iniciada",
            ["it"] = "Installazione avviata",
        },
        ["upd.installing_detail"] = new()
        {
            ["en"] = "Restart Revit when it finishes.",
            ["es"] = "Vuelva a abrir Revit cuando termine.",
            ["it"] = "Al termine riavvia Revit.",
        },
        ["upd.close_revit"] = new()
        {
            ["en"] = "Close Revit now",
            ["es"] = "Cerrar Revit ahora",
            ["it"] = "Chiudi Revit ora",
        },
        ["upd.failed"] = new()
        {
            ["en"] = "Download failed",
            ["es"] = "La descarga falló",
            ["it"] = "Download fallito",
        },
        ["upd.unknown_error"] = new()
        {
            ["en"] = "Unknown error",
            ["es"] = "Error desconocido",
            ["it"] = "Errore sconosciuto",
        },
        ["upd.retry"] = new()
        {
            ["en"] = "Retry",
            ["es"] = "Reintentar",
            ["it"] = "Riprova",
        },
        ["upd.download_install"] = new()
        {
            ["en"] = "Download & install",
            ["es"] = "Descargar e instalar",
            ["it"] = "Scarica e installa",
        },
        ["upd.ready_warning"] = new()
        {
            ["en"] = "⚠ Revit will close automatically — save your work before continuing.",
            ["es"] = "⚠ Revit se va a cerrar automáticamente — guarde su trabajo antes de continuar.",
            ["it"] = "⚠ Revit verrà chiuso automaticamente — salva il lavoro prima di continuare.",
        },
        ["upd.install_and_close"] = new()
        {
            ["en"] = "Install and close Revit",
            ["es"] = "Instalar y cerrar Revit",
            ["it"] = "Installa e chiudi Revit",
        },
        ["upd.installing_closing"] = new()
        {
            ["en"] = "Installation started — closing…",
            ["es"] = "Instalación iniciada — cerrando…",
            ["it"] = "Installazione avviata — chiusura in corso…",
        },

        // ── Redesigned ribbon: Vortex Switch label ──────────────────────
        ["ribbon.connect.text_off"] = new()
        {
            ["en"] = "Vortex\r\nSwitch",
            ["es"] = "Vortex\r\nSwitch",
            ["it"] = "Vortex\r\nSwitch",
        },
        ["ribbon.connect.text_on"] = new()
        {
            ["en"] = "Vortex\r\nON",
            ["es"] = "Vortex\r\nACTIVO",
            ["it"] = "Vortex\r\nATTIVO",
        },

        // ── Autopilot pill ──────────────────────────────────────────────
        ["pill.start_server"] = new()
        {
            ["en"] = "Start",
            ["es"] = "Iniciar",
            ["it"] = "Avvia",
        },
        ["pill.expand_tip"] = new()
        {
            ["en"] = "Show activity",
            ["es"] = "Ver la actividad",
            ["it"] = "Mostra l'attività",
        },
        ["pill.collapse_tip"] = new()
        {
            ["en"] = "Hide activity",
            ["es"] = "Ocultar la actividad",
            ["it"] = "Nascondi l'attività",
        },
        ["pill.scripts_tip"] = new()
        {
            ["en"] = "C# scripts are allowed in this session",
            ["es"] = "Los scripts C# están permitidos en esta sesión",
            ["it"] = "Gli script C# sono consentiti in questa sessione",
        },
        ["pill.approved_tip"] = new()
        {
            ["en"] = "Edits approved automatically",
            ["es"] = "Modificaciones aprobadas automáticamente",
            ["it"] = "Modifiche approvate automaticamente",
        },
        ["pill.declined_tip"] = new()
        {
            ["en"] = "Steps declined: they need your confirmation",
            ["es"] = "Pasos rechazados: necesitan su confirmación",
            ["it"] = "Passaggi rifiutati: richiedono la tua conferma",
        },
        ["pill.warn_server"] = new()
        {
            ["en"] = "Server off: the AI cannot connect",
            ["es"] = "Servidor apagado: la IA no puede conectarse",
            ["it"] = "Server spento: l'IA non può connettersi",
        },
        ["pill.warn_paused"] = new()
        {
            ["en"] = "Another document is active: approvals paused",
            ["es"] = "Otro documento activo: aprobaciones en pausa",
            ["it"] = "Altro documento attivo: approvazioni in pausa",
        },
        ["pill.unsaved"] = new()
        {
            ["en"] = "Unsaved",
            ["es"] = "Sin guardar",
            ["it"] = "Non salvato",
        },
        ["pill.saved"] = new()
        {
            ["en"] = "Saved {0}",
            ["es"] = "Guardado {0}",
            ["it"] = "Salvato {0}",
        },
        ["pill.ev_approved"] = new()
        {
            ["en"] = "Approved: {0} ({1})",
            ["es"] = "Aprobado: {0} ({1})",
            ["it"] = "Approvato: {0} ({1})",
        },
        ["pill.ev_declined"] = new()
        {
            ["en"] = "Declined: {0} ({1})",
            ["es"] = "Rechazado: {0} ({1})",
            ["it"] = "Rifiutato: {0} ({1})",
        },
        ["pill.ev_dialog"] = new()
        {
            ["en"] = "Revit dialog closed with {0}",
            ["es"] = "Aviso de Revit cerrado con {0}",
            ["it"] = "Avviso di Revit chiuso con {0}",
        },
        ["pill.ev_saved"] = new()
        {
            ["en"] = "Model saved",
            ["es"] = "Modelo guardado",
            ["it"] = "Modello salvato",
        },

        // ── Autopilot activity panel ────────────────────────────────────
        ["panel.open_log"] = new()
        {
            ["en"] = "Open the full log",
            ["es"] = "Abrir el registro completo",
            ["it"] = "Apri il registro completo",
        },
        ["panel.stat_approved"] = new()
        {
            ["en"] = "Approved",
            ["es"] = "Aprobadas",
            ["it"] = "Approvate",
        },
        ["panel.stat_declined"] = new()
        {
            ["en"] = "Declined",
            ["es"] = "Rechazadas",
            ["it"] = "Rifiutate",
        },
        ["panel.stat_dialogs"] = new()
        {
            ["en"] = "Dialogs",
            ["es"] = "Avisos",
            ["it"] = "Avvisi",
        },
        ["panel.stat_saved"] = new()
        {
            ["en"] = "Saved",
            ["es"] = "Guardado",
            ["it"] = "Salvato",
        },
        ["panel.events"] = new()
        {
            ["en"] = "Latest events",
            ["es"] = "Últimos eventos",
            ["it"] = "Ultimi eventi",
        },
        ["panel.uptime"] = new()
        {
            ["en"] = "On for {0}",
            ["es"] = "Encendido hace {0}",
            ["it"] = "Attivo da {0}",
        },
        ["panel.last_activity"] = new()
        {
            ["en"] = "Last activity {0} ago",
            ["es"] = "Última actividad hace {0}",
            ["it"] = "Ultima attività {0} fa",
        },
        ["panel.no_activity"] = new()
        {
            ["en"] = "No activity yet",
            ["es"] = "Sin actividad todavía",
            ["it"] = "Nessuna attività finora",
        },
        ["panel.no_events"] = new()
        {
            ["en"] = "Nothing has happened yet. Approvals, closed dialogs and saves will show up here.",
            ["es"] = "Todavía no pasó nada. Aquí van a aparecer las aprobaciones, los avisos cerrados y los guardados.",
            ["it"] = "Non è ancora successo nulla. Qui compariranno approvazioni, avvisi chiusi e salvataggi.",
        },
        ["time.less_minute"] = new()
        {
            ["en"] = "under 1 min",
            ["es"] = "menos de 1 min",
            ["it"] = "meno di 1 min",
        },
        ["time.minutes"] = new()
        {
            ["en"] = "{0} min",
            ["es"] = "{0} min",
            ["it"] = "{0} min",
        },
        ["time.hours"] = new()
        {
            ["en"] = "{0} h {1} min",
            ["es"] = "{0} h {1} min",
            ["it"] = "{0} h {1} min",
        },

        // ── Autopilot summary (shown when it stops) ─────────────────────
        ["summary.title"] = new()
        {
            ["en"] = "Autopilot stopped",
            ["es"] = "Piloto automático detenido",
            ["it"] = "Pilota automatico fermato",
        },
        ["summary.duration"] = new()
        {
            ["en"] = "It was on for {0}, from {1} to {2}.",
            ["es"] = "Estuvo encendido {0}, de {1} a {2}.",
            ["it"] = "È rimasto attivo {0}, dalle {1} alle {2}.",
        },
        ["summary.pending"] = new()
        {
            ["en"] = "Left for you to confirm",
            ["es"] = "Quedó pendiente de su confirmación",
            ["it"] = "In attesa della tua conferma",
        },
        ["summary.pending_note"] = new()
        {
            ["en"] = "The AI carried on with the rest of the task and noted these steps.",
            ["es"] = "La IA siguió con el resto de la tarea y dejó estos pasos anotados.",
            ["it"] = "L'IA ha proseguito con il resto dell'attività e ha annotato questi passaggi.",
        },
        ["summary.nothing_pending"] = new()
        {
            ["en"] = "Nothing was left pending.",
            ["es"] = "No quedó nada pendiente.",
            ["it"] = "Non è rimasto nulla in sospeso.",
        },
        ["summary.open_log"] = new()
        {
            ["en"] = "Open the log",
            ["es"] = "Abrir el registro",
            ["it"] = "Apri il registro",
        },
        ["summary.done"] = new()
        {
            ["en"] = "Done",
            ["es"] = "Listo",
            ["it"] = "Fatto",
        },

        // ── Update notice (redesign) ────────────────────────────────────
        ["upd.notes_heading"] = new()
        {
            ["en"] = "What's new",
            ["es"] = "Novedades",
            ["it"] = "Novità",
        },
        ["upd.downloading_title"] = new()
        {
            ["en"] = "Downloading RVT Vortex {0}",
            ["es"] = "Descargando RVT Vortex {0}",
            ["it"] = "Download di RVT Vortex {0}",
        },
        ["upd.downloading_detail"] = new()
        {
            ["en"] = "You can keep working in Revit.",
            ["es"] = "Puede seguir trabajando en Revit.",
            ["it"] = "Puoi continuare a lavorare in Revit.",
        },
        ["upd.downloaded_detail"] = new()
        {
            ["en"] = "RVT Vortex {0} is downloaded.",
            ["es"] = "RVT Vortex {0} ya está descargado.",
            ["it"] = "RVT Vortex {0} è stato scaricato.",
        },
        ["upd.manual_download"] = new()
        {
            ["en"] = "Download manually",
            ["es"] = "Descargar manualmente",
            ["it"] = "Scarica manualmente",
        },

        // ── Settings window ─────────────────────────────────────────────
        ["settings.window_title"] = new()
        {
            ["en"] = "RVT Vortex Settings",
            ["es"] = "Configuración de RVT Vortex",
            ["it"] = "Impostazioni di RVT Vortex",
        },
        ["settings.title"] = new()
        {
            ["en"] = "Settings",
            ["es"] = "Configuración",
            ["it"] = "Impostazioni",
        },
        ["settings.tab_general"] = new()
        {
            ["en"] = "General",
            ["es"] = "General",
            ["it"] = "Generale",
        },
        ["settings.tab_tools"] = new()
        {
            ["en"] = "Tools",
            ["es"] = "Herramientas",
            ["it"] = "Strumenti",
        },
        ["settings.subtitle"] = new()
        {
            ["en"] = "RVT Vortex {0} · Revit {1}",
            ["es"] = "RVT Vortex {0} · Revit {1}",
            ["it"] = "RVT Vortex {0} · Revit {1}",
        },
        ["settings.subtitle_no_revit"] = new()
        {
            ["en"] = "RVT Vortex {0}",
            ["es"] = "RVT Vortex {0}",
            ["it"] = "RVT Vortex {0}",
        },
        ["settings.sec_server"] = new()
        {
            ["en"] = "Server",
            ["es"] = "Servidor",
            ["it"] = "Server",
        },
        ["settings.sec_server_help"] = new()
        {
            ["en"] = "The connection between Revit and the AI.",
            ["es"] = "La conexión entre Revit y la IA.",
            ["it"] = "La connessione tra Revit e l'IA.",
        },
        ["settings.server_on"] = new()
        {
            ["en"] = "Running",
            ["es"] = "Activo",
            ["it"] = "Attivo",
        },
        ["settings.server_on_detail"] = new()
        {
            ["en"] = "The AI can work in this model. Port {0}.",
            ["es"] = "La IA puede trabajar en este modelo. Puerto {0}.",
            ["it"] = "L'IA può lavorare su questo modello. Porta {0}.",
        },
        ["settings.server_off"] = new()
        {
            ["en"] = "Stopped",
            ["es"] = "Apagado",
            ["it"] = "Spento",
        },
        ["settings.server_off_detail"] = new()
        {
            ["en"] = "The AI cannot connect until you start it.",
            ["es"] = "La IA no puede conectarse hasta que lo inicie.",
            ["it"] = "L'IA non può connettersi finché non lo avvii.",
        },
        ["settings.server_start"] = new()
        {
            ["en"] = "Start",
            ["es"] = "Iniciar",
            ["it"] = "Avvia",
        },
        ["settings.server_stop"] = new()
        {
            ["en"] = "Stop",
            ["es"] = "Detener",
            ["it"] = "Ferma",
        },
        ["settings.server_start_failed"] = new()
        {
            ["en"] = "The server did not start. Check that the port is free and try again.",
            ["es"] = "El servidor no se inició. Verifique que el puerto esté libre y vuelva a intentar.",
            ["it"] = "Il server non si è avviato. Verifica che la porta sia libera e riprova.",
        },
        ["settings.port"] = new()
        {
            ["en"] = "Port",
            ["es"] = "Puerto",
            ["it"] = "Porta",
        },
        ["settings.port_hint"] = new()
        {
            ["en"] = "Applies after restarting Revit.",
            ["es"] = "Se aplica al reiniciar Revit.",
            ["it"] = "Si applica al riavvio di Revit.",
        },
        ["settings.sec_log"] = new()
        {
            ["en"] = "Logging",
            ["es"] = "Registro",
            ["it"] = "Registro",
        },
        ["settings.sec_log_help"] = new()
        {
            ["en"] = "How much detail the plugin writes to its logs.",
            ["es"] = "Cuánto detalle escribe el plugin en sus registros.",
            ["it"] = "Quanto dettaglio scrive il plugin nei suoi registri.",
        },
        ["settings.sec_protect"] = new()
        {
            ["en"] = "Protection",
            ["es"] = "Protección",
            ["it"] = "Protezione",
        },
        ["settings.sec_protect_help"] = new()
        {
            ["en"] = "Prevents accidental changes to the model.",
            ["es"] = "Evita cambios accidentales en el modelo.",
            ["it"] = "Evita modifiche accidentali al modello.",
        },
        ["settings.readonly"] = new()
        {
            ["en"] = "Read-only mode",
            ["es"] = "Modo solo lectura",
            ["it"] = "Modalità sola lettura",
        },
        ["settings.readonly_help"] = new()
        {
            ["en"] = "Blocks every tool that modifies the model.",
            ["es"] = "Bloquea todas las herramientas que modifican el modelo.",
            ["it"] = "Blocca tutti gli strumenti che modificano il modello.",
        },
        ["settings.sec_reports"] = new()
        {
            ["en"] = "Error reports",
            ["es"] = "Reportes de error",
            ["it"] = "Report degli errori",
        },
        ["settings.sec_reports_help"] = new()
        {
            ["en"] = "Diagnostic packages saved on this computer.",
            ["es"] = "Paquetes de diagnóstico guardados en este equipo.",
            ["it"] = "Pacchetti diagnostici salvati su questo computer.",
        },
        ["settings.keep_label"] = new()
        {
            ["en"] = "Keep the last",
            ["es"] = "Conservar los últimos",
            ["it"] = "Conserva gli ultimi",
        },
        ["settings.keep_suffix"] = new()
        {
            ["en"] = "reports",
            ["es"] = "reportes",
            ["it"] = "report",
        },
        ["settings.delete_reports"] = new()
        {
            ["en"] = "Delete all",
            ["es"] = "Borrar todos",
            ["it"] = "Elimina tutti",
        },
        ["settings.reset"] = new()
        {
            ["en"] = "Reset",
            ["es"] = "Restablecer",
            ["it"] = "Ripristina",
        },
        ["settings.save"] = new()
        {
            ["en"] = "Save",
            ["es"] = "Guardar",
            ["it"] = "Salva",
        },
        ["settings.footer_hint"] = new()
        {
            ["en"] = "Changes apply when you save.",
            ["es"] = "Los cambios se aplican al guardar.",
            ["it"] = "Le modifiche si applicano al salvataggio.",
        },
        ["settings.saved"] = new()
        {
            ["en"] = "Saved ✓",
            ["es"] = "Guardado ✓",
            ["it"] = "Salvato ✓",
        },
        ["settings.saved_restart"] = new()
        {
            ["en"] = "Saved ✓  Restart Revit to use the new port.",
            ["es"] = "Guardado ✓  Reinicie Revit para usar el puerto nuevo.",
            ["it"] = "Salvato ✓  Riavvia Revit per usare la nuova porta.",
        },
        ["settings.save_failed"] = new()
        {
            ["en"] = "Could not save: {0}",
            ["es"] = "No se pudo guardar: {0}",
            ["it"] = "Impossibile salvare: {0}",
        },
        ["settings.invalid_port"] = new()
        {
            ["en"] = "The port must be a number between 1 and 65535.",
            ["es"] = "El puerto debe ser un número entre 1 y 65535.",
            ["it"] = "La porta deve essere un numero tra 1 e 65535.",
        },

        // ── Settings: tools tab ─────────────────────────────────────────
        ["tools.sec_scripts"] = new()
        {
            ["en"] = "C# scripts",
            ["es"] = "Scripts C#",
            ["it"] = "Script C#",
        },
        ["tools.scripts_toggle"] = new()
        {
            ["en"] = "Let the AI run scripts",
            ["es"] = "Permitir que la IA ejecute scripts",
            ["it"] = "Consenti all'IA di eseguire script",
        },
        ["tools.scripts_help"] = new()
        {
            ["en"] = "Last resort. A script runs inside Revit with your Windows permissions: the filter rejects common file and network calls, but it does not isolate the script.",
            ["es"] = "Último recurso. Un script corre dentro de Revit con sus permisos de Windows: el filtro rechaza llamadas comunes a archivos y red, pero no lo aísla.",
            ["it"] = "Ultima risorsa. Uno script viene eseguito dentro Revit con i tuoi permessi di Windows: il filtro rifiuta le chiamate comuni a file e rete, ma non lo isola.",
        },
        ["tools.heading"] = new()
        {
            ["en"] = "Tools",
            ["es"] = "Herramientas",
            ["it"] = "Strumenti",
        },
        ["tools.count"] = new()
        {
            ["en"] = "{0} of {1} enabled",
            ["es"] = "{0} de {1} activas",
            ["it"] = "{0} di {1} attivi",
        },
        ["tools.search"] = new()
        {
            ["en"] = "Search tools…",
            ["es"] = "Buscar herramienta…",
            ["it"] = "Cerca strumento…",
        },
        ["tools.filter_all"] = new()
        {
            ["en"] = "All",
            ["es"] = "Todas",
            ["it"] = "Tutti",
        },
        ["tools.filter_on"] = new()
        {
            ["en"] = "Enabled",
            ["es"] = "Activas",
            ["it"] = "Attivi",
        },
        ["tools.filter_off"] = new()
        {
            ["en"] = "Disabled",
            ["es"] = "Desactivadas",
            ["it"] = "Disattivati",
        },
        ["tools.enable_all"] = new()
        {
            ["en"] = "Enable all",
            ["es"] = "Activar todas",
            ["it"] = "Attiva tutti",
        },
        ["tools.disable_all"] = new()
        {
            ["en"] = "Disable all",
            ["es"] = "Desactivar todas",
            ["it"] = "Disattiva tutti",
        },
        ["tools.group_count"] = new()
        {
            ["en"] = "{0} of {1}",
            ["es"] = "{0} de {1}",
            ["it"] = "{0} di {1}",
        },
        ["tools.group_disable"] = new()
        {
            ["en"] = "Disable group",
            ["es"] = "Desactivar grupo",
            ["it"] = "Disattiva gruppo",
        },
        ["tools.group_enable"] = new()
        {
            ["en"] = "Enable group",
            ["es"] = "Activar grupo",
            ["it"] = "Attiva gruppo",
        },
        ["tools.none_loaded"] = new()
        {
            ["en"] = "No tools loaded",
            ["es"] = "No hay herramientas cargadas",
            ["it"] = "Nessuno strumento caricato",
        },
        ["tools.none_loaded_help"] = new()
        {
            ["en"] = "RVT Vortex did not finish starting. Restart Revit and open this window again.",
            ["es"] = "RVT Vortex no terminó de iniciar. Reinicie Revit y vuelva a abrir esta ventana.",
            ["it"] = "RVT Vortex non ha completato l'avvio. Riavvia Revit e riapri questa finestra.",
        },
        ["tools.no_match"] = new()
        {
            ["en"] = "No tools match",
            ["es"] = "Ninguna herramienta coincide",
            ["it"] = "Nessuno strumento corrisponde",
        },
        ["tools.no_match_help"] = new()
        {
            ["en"] = "Try another word, or switch the filter back to All.",
            ["es"] = "Pruebe con otra palabra o vuelva el filtro a Todas.",
            ["it"] = "Prova con un'altra parola o riporta il filtro su Tutti.",
        },
        ["tools.saved"] = new()
        {
            ["en"] = "Saved ✓  {0} tools disabled.",
            ["es"] = "Guardado ✓  {0} herramientas desactivadas.",
            ["it"] = "Salvato ✓  {0} strumenti disattivati.",
        },
        ["tools.saved_scripts_on"] = new()
        {
            ["en"] = "Saved ✓  {0} tools disabled · C# scripts allowed.",
            ["es"] = "Guardado ✓  {0} herramientas desactivadas · scripts C# permitidos.",
            ["it"] = "Salvato ✓  {0} strumenti disattivati · script C# consentiti.",
        },

        // ── Power BI export window (en / es / it) ───────────────────────
        // Window, steps, footer
        ["pbi.window_title"] = new()
        {
            ["en"] = "RVT Vortex",
            ["es"] = "RVT Vortex",
            ["it"] = "RVT Vortex",
        },
        ["pbi.title"] = new()
        {
            ["en"] = "Export to Power BI",
            ["es"] = "Exportar a Power BI",
            ["it"] = "Esporta in Power BI",
        },
        ["pbi.subtitle_data"] = new()
        {
            ["en"] = "Choose which model data goes into the CSV file.",
            ["es"] = "Elija qué datos del modelo van al archivo CSV.",
            ["it"] = "Scegli quali dati del modello vanno nel file CSV.",
        },
        ["pbi.subtitle_output"] = new()
        {
            ["en"] = "Where the file is saved and how it stays up to date.",
            ["es"] = "Dónde se guarda el archivo y cómo se mantiene al día.",
            ["it"] = "Dove viene salvato il file e come resta aggiornato.",
        },
        ["pbi.step_data"] = new()
        {
            ["en"] = "Data",
            ["es"] = "Datos",
            ["it"] = "Dati",
        },
        ["pbi.step_output"] = new()
        {
            ["en"] = "Output",
            ["es"] = "Salida",
            ["it"] = "Output",
        },
        ["pbi.back"] = new()
        {
            ["en"] = "Back",
            ["es"] = "Atrás",
            ["it"] = "Indietro",
        },
        ["pbi.next"] = new()
        {
            ["en"] = "Next",
            ["es"] = "Siguiente",
            ["it"] = "Avanti",
        },
        ["pbi.export"] = new()
        {
            ["en"] = "Export",
            ["es"] = "Exportar",
            ["it"] = "Esporta",
        },
        ["pbi.cancel"] = new()
        {
            ["en"] = "Cancel",
            ["es"] = "Cancelar",
            ["it"] = "Annulla",
        },
        ["pbi.save"] = new()
        {
            ["en"] = "Save",
            ["es"] = "Guardar",
            ["it"] = "Salva",
        },
        ["pbi.open_failed"] = new()
        {
            ["en"] = "The window could not be opened.",
            ["es"] = "No se pudo abrir la ventana.",
            ["it"] = "Impossibile aprire la finestra.",
        },
        // Counts (each has a _one and a _many form)
        ["pbi.count.categories_one"] = new()
        {
            ["en"] = "{0} category",
            ["es"] = "{0} categoría",
            ["it"] = "{0} categoria",
        },
        ["pbi.count.categories_many"] = new()
        {
            ["en"] = "{0} categories",
            ["es"] = "{0} categorías",
            ["it"] = "{0} categorie",
        },
        ["pbi.count.columns_one"] = new()
        {
            ["en"] = "{0} column",
            ["es"] = "{0} columna",
            ["it"] = "{0} colonna",
        },
        ["pbi.count.columns_many"] = new()
        {
            ["en"] = "{0} columns",
            ["es"] = "{0} columnas",
            ["it"] = "{0} colonne",
        },
        ["pbi.count.schedules_one"] = new()
        {
            ["en"] = "{0} schedule",
            ["es"] = "{0} tabla de planificación",
            ["it"] = "{0} abaco",
        },
        ["pbi.count.schedules_many"] = new()
        {
            ["en"] = "{0} schedules",
            ["es"] = "{0} tablas de planificación",
            ["it"] = "{0} abachi",
        },
        ["pbi.count.chosen_f_one"] = new()
        {
            ["en"] = "{0} selected",
            ["es"] = "{0} elegida",
            ["it"] = "{0} selezionata",
        },
        ["pbi.count.chosen_f_many"] = new()
        {
            ["en"] = "{0} selected",
            ["es"] = "{0} elegidas",
            ["it"] = "{0} selezionate",
        },
        ["pbi.count.schedules_chosen_one"] = new()
        {
            ["en"] = "{0} selected",
            ["es"] = "{0} elegida",
            ["it"] = "{0} selezionato",
        },
        ["pbi.count.schedules_chosen_many"] = new()
        {
            ["en"] = "{0} selected",
            ["es"] = "{0} elegidas",
            ["it"] = "{0} selezionati",
        },
        ["pbi.rows_one"] = new()
        {
            ["en"] = "{0:N0} row",
            ["es"] = "{0:N0} fila",
            ["it"] = "{0:N0} riga",
        },
        ["pbi.rows_many"] = new()
        {
            ["en"] = "{0:N0} rows",
            ["es"] = "{0:N0} filas",
            ["it"] = "{0:N0} righe",
        },
        // Step 1: where the rows come from
        ["pbi.scope.whole"] = new()
        {
            ["en"] = "Whole model",
            ["es"] = "Todo el modelo",
            ["it"] = "Tutto il modello",
        },
        ["pbi.scope.whole_help"] = new()
        {
            ["en"] = "Every element of the project",
            ["es"] = "Todos los elementos del proyecto",
            ["it"] = "Tutti gli elementi del progetto",
        },
        ["pbi.scope.view"] = new()
        {
            ["en"] = "Active view",
            ["es"] = "Vista activa",
            ["it"] = "Vista attiva",
        },
        ["pbi.scope.view_help"] = new()
        {
            ["en"] = "Only what the current view shows",
            ["es"] = "Solo lo visible en la vista actual",
            ["it"] = "Solo ciò che è visibile nella vista corrente",
        },
        ["pbi.scope.selection"] = new()
        {
            ["en"] = "Current selection",
            ["es"] = "Selección actual",
            ["it"] = "Selezione corrente",
        },
        ["pbi.scope.selection_help"] = new()
        {
            ["en"] = "The elements selected in Revit",
            ["es"] = "Los elementos seleccionados en Revit",
            ["it"] = "Gli elementi selezionati in Revit",
        },
        ["pbi.scope.schedules"] = new()
        {
            ["en"] = "Schedules",
            ["es"] = "Tablas de planificación",
            ["it"] = "Abachi",
        },
        ["pbi.scope.schedules_help"] = new()
        {
            ["en"] = "Reuse the schedules that already exist",
            ["es"] = "Reutiliza las tablas que ya existen",
            ["it"] = "Riusa gli abachi già presenti",
        },
        // Step 1: categories
        ["pbi.categories"] = new()
        {
            ["en"] = "Categories",
            ["es"] = "Categorías",
            ["it"] = "Categorie",
        },
        ["pbi.categories.model"] = new()
        {
            ["en"] = "Model",
            ["es"] = "Modelo",
            ["it"] = "Modello",
        },
        ["pbi.categories.annotation"] = new()
        {
            ["en"] = "Annotation",
            ["es"] = "Anotación",
            ["it"] = "Annotazione",
        },
        ["pbi.categories.analytical"] = new()
        {
            ["en"] = "Analytical",
            ["es"] = "Analítico",
            ["it"] = "Analitico",
        },
        ["pbi.categories.other"] = new()
        {
            ["en"] = "Other",
            ["es"] = "Otras",
            ["it"] = "Altre",
        },
        ["pbi.categories.none_in_model"] = new()
        {
            ["en"] = "The model has no categories with elements.",
            ["es"] = "El modelo no tiene categorías con elementos.",
            ["it"] = "Il modello non ha categorie con elementi.",
        },
        ["pbi.categories.none_here"] = new()
        {
            ["en"] = "No categories here.",
            ["es"] = "No hay categorías aquí.",
            ["it"] = "Nessuna categoria qui.",
        },
        ["pbi.select_all"] = new()
        {
            ["en"] = "Select all",
            ["es"] = "Elegir todas",
            ["it"] = "Seleziona tutto",
        },
        ["pbi.select_none"] = new()
        {
            ["en"] = "Clear all",
            ["es"] = "Quitar todas",
            ["it"] = "Deseleziona tutto",
        },
        // Step 1: parameters and columns
        ["pbi.available"] = new()
        {
            ["en"] = "Available parameters",
            ["es"] = "Parámetros disponibles",
            ["it"] = "Parametri disponibili",
        },
        ["pbi.filter"] = new()
        {
            ["en"] = "Filter…",
            ["es"] = "Filtrar…",
            ["it"] = "Filtra…",
        },
        ["pbi.filter_tip"] = new()
        {
            ["en"] = "Filter by name or group",
            ["es"] = "Filtrar por nombre o grupo",
            ["it"] = "Filtra per nome o gruppo",
        },
        ["pbi.include_type"] = new()
        {
            ["en"] = "Type parameters",
            ["es"] = "Parámetros de tipo",
            ["it"] = "Parametri di tipo",
        },
        ["pbi.include_type_tip"] = new()
        {
            ["en"] = "Also list the parameters of each element's type.",
            ["es"] = "Incluye también los parámetros del tipo de cada elemento.",
            ["it"] = "Include anche i parametri del tipo di ogni elemento.",
        },
        ["pbi.hide_empty"] = new()
        {
            ["en"] = "Hide empty ones",
            ["es"] = "Ocultar los vacíos",
            ["it"] = "Nascondi i vuoti",
        },
        ["pbi.hide_empty_tip"] = new()
        {
            ["en"] = "Hides the parameters that have no value on any sampled element.",
            ["es"] = "Oculta los parámetros que no tienen valor en ningún elemento de la muestra.",
            ["it"] = "Nasconde i parametri senza valore in tutti gli elementi campionati.",
        },
        ["pbi.available.pick_category"] = new()
        {
            ["en"] = "Tick one or more categories to see their parameters.",
            ["es"] = "Elija una o más categorías para ver sus parámetros.",
            ["it"] = "Seleziona una o più categorie per vederne i parametri.",
        },
        ["pbi.available.all_added"] = new()
        {
            ["en"] = "Every parameter is already a column.",
            ["es"] = "Todos los parámetros ya son columnas.",
            ["it"] = "Tutti i parametri sono già colonne.",
        },
        ["pbi.available.no_match"] = new()
        {
            ["en"] = "No parameters match. Try another word, or show the empty ones.",
            ["es"] = "Ningún parámetro coincide. Pruebe con otra palabra o muestre los vacíos.",
            ["it"] = "Nessun parametro corrisponde. Prova con un'altra parola o mostra i vuoti.",
        },
        ["pbi.move.add"] = new()
        {
            ["en"] = "Add the selected parameters",
            ["es"] = "Agregar los seleccionados",
            ["it"] = "Aggiungi i selezionati",
        },
        ["pbi.move.add_all"] = new()
        {
            ["en"] = "Add all the listed parameters",
            ["es"] = "Agregar todos los visibles",
            ["it"] = "Aggiungi tutti i visibili",
        },
        ["pbi.move.remove"] = new()
        {
            ["en"] = "Remove the selected columns",
            ["es"] = "Quitar los seleccionados",
            ["it"] = "Rimuovi i selezionati",
        },
        ["pbi.move.remove_all"] = new()
        {
            ["en"] = "Remove all columns",
            ["es"] = "Quitar todos",
            ["it"] = "Rimuovi tutti",
        },
        ["pbi.move.up"] = new()
        {
            ["en"] = "Move up",
            ["es"] = "Subir",
            ["it"] = "Sposta su",
        },
        ["pbi.move.down"] = new()
        {
            ["en"] = "Move down",
            ["es"] = "Bajar",
            ["it"] = "Sposta giù",
        },
        ["pbi.columns"] = new()
        {
            ["en"] = "CSV columns",
            ["es"] = "Columnas del CSV",
            ["it"] = "Colonne del CSV",
        },
        ["pbi.columns.caption_none"] = new()
        {
            ["en"] = "No columns yet.",
            ["es"] = "Todavía no hay columnas.",
            ["it"] = "Ancora nessuna colonna.",
        },
        ["pbi.columns.caption_one"] = new()
        {
            ["en"] = "{0} parameter.",
            ["es"] = "{0} parámetro.",
            ["it"] = "{0} parametro.",
        },
        ["pbi.columns.caption_many"] = new()
        {
            ["en"] = "{0} parameters, in the order they appear in the file.",
            ["es"] = "{0} parámetros, en el orden en que salen en el archivo.",
            ["it"] = "{0} parametri, nell'ordine in cui compaiono nel file.",
        },
        ["pbi.columns.empty"] = new()
        {
            ["en"] = "Add parameters with the arrows or a double click.",
            ["es"] = "Agregue parámetros con las flechas o con doble clic.",
            ["it"] = "Aggiungi parametri con le frecce o con un doppio clic.",
        },
        ["pbi.badge.type"] = new()
        {
            ["en"] = "type",
            ["es"] = "tipo",
            ["it"] = "tipo",
        },
        ["pbi.badge.calculated"] = new()
        {
            ["en"] = "calculated",
            ["es"] = "calculado",
            ["it"] = "calcolato",
        },
        ["pbi.param.instance"] = new()
        {
            ["en"] = "Instance parameter",
            ["es"] = "Parámetro de ejemplar",
            ["it"] = "Parametro di istanza",
        },
        ["pbi.param.type"] = new()
        {
            ["en"] = "Type parameter",
            ["es"] = "Parámetro de tipo",
            ["it"] = "Parametro di tipo",
        },
        ["pbi.param.coverage"] = new()
        {
            ["en"] = "{0}% of the sampled elements have a value",
            ["es"] = "{0} % de la muestra tiene valor",
            ["it"] = "{0}% del campione ha un valore",
        },
        ["pbi.param.read_only"] = new()
        {
            ["en"] = "read-only",
            ["es"] = "solo lectura",
            ["it"] = "sola lettura",
        },
        ["pbi.param.shared"] = new()
        {
            ["en"] = "shared",
            ["es"] = "compartido",
            ["it"] = "condiviso",
        },
        ["pbi.need_category"] = new()
        {
            ["en"] = "Tick at least one category.",
            ["es"] = "Elija al menos una categoría.",
            ["it"] = "Seleziona almeno una categoria.",
        },
        ["pbi.need_parameter"] = new()
        {
            ["en"] = "Add at least one parameter to the CSV columns.",
            ["es"] = "Agregue al menos un parámetro a las columnas del CSV.",
            ["it"] = "Aggiungi almeno un parametro alle colonne del CSV.",
        },
        ["pbi.need_schedule"] = new()
        {
            ["en"] = "Tick at least one schedule.",
            ["es"] = "Elija al menos una tabla de planificación.",
            ["it"] = "Seleziona almeno un abaco.",
        },
        // Step 1: schedules
        ["pbi.schedules"] = new()
        {
            ["en"] = "Schedules",
            ["es"] = "Tablas de planificación",
            ["it"] = "Abachi",
        },
        ["pbi.schedules.empty"] = new()
        {
            ["en"] = "The model has no schedules.",
            ["es"] = "El modelo no tiene tablas de planificación.",
            ["it"] = "Il modello non ha abachi.",
        },
        ["pbi.schedule_columns"] = new()
        {
            ["en"] = "Columns",
            ["es"] = "Columnas",
            ["it"] = "Colonne",
        },
        ["pbi.schedule_columns_of"] = new()
        {
            ["en"] = "Columns of {0}",
            ["es"] = "Columnas de {0}",
            ["it"] = "Colonne di {0}",
        },
        ["pbi.schedule_columns.pick"] = new()
        {
            ["en"] = "Tick a schedule to see its columns.",
            ["es"] = "Elija una tabla para ver sus columnas.",
            ["it"] = "Seleziona un abaco per vederne le colonne.",
        },
        ["pbi.schedule_columns.unreadable"] = new()
        {
            ["en"] = "This schedule could not be read.",
            ["es"] = "No se pudo leer esta tabla.",
            ["it"] = "Impossibile leggere questo abaco.",
        },
        ["pbi.schedule_columns.none"] = new()
        {
            ["en"] = "This schedule has no visible columns.",
            ["es"] = "Esta tabla no tiene columnas visibles.",
            ["it"] = "Questo abaco non ha colonne visibili.",
        },
        // Step 2: file
        ["pbi.file"] = new()
        {
            ["en"] = "File",
            ["es"] = "Archivo",
            ["it"] = "File",
        },
        ["pbi.file_help"] = new()
        {
            ["en"] = "Where the CSV is saved.",
            ["es"] = "Dónde se guarda el CSV.",
            ["it"] = "Dove viene salvato il CSV.",
        },
        ["pbi.output_folder"] = new()
        {
            ["en"] = "Output folder",
            ["es"] = "Carpeta de salida",
            ["it"] = "Cartella di output",
        },
        ["pbi.browse"] = new()
        {
            ["en"] = "Browse…",
            ["es"] = "Examinar…",
            ["it"] = "Sfoglia…",
        },
        ["pbi.browse_tip"] = new()
        {
            ["en"] = "Choose another folder",
            ["es"] = "Elegir otra carpeta",
            ["it"] = "Scegli un'altra cartella",
        },
        ["pbi.browse_title"] = new()
        {
            ["en"] = "Choose the output folder (go into it and press Open)",
            ["es"] = "Elija la carpeta de salida (entre en ella y presione Abrir)",
            ["it"] = "Scegli la cartella di output (entra e premi Apri)",
        },
        ["pbi.browse_placeholder"] = new()
        {
            ["en"] = "Select-this-folder",
            ["es"] = "Elegir-esta-carpeta",
            ["it"] = "Seleziona-questa-cartella",
        },
        ["pbi.browse_filter"] = new()
        {
            ["en"] = "Folder",
            ["es"] = "Carpeta",
            ["it"] = "Cartella",
        },
        ["pbi.open"] = new()
        {
            ["en"] = "Open",
            ["es"] = "Abrir",
            ["it"] = "Apri",
        },
        ["pbi.open_tip"] = new()
        {
            ["en"] = "Opens this folder in File Explorer, to check the files already exported.",
            ["es"] = "Abre esta carpeta en el Explorador, para revisar los archivos ya exportados.",
            ["it"] = "Apre questa cartella in Esplora file, per controllare i file già esportati.",
        },
        ["pbi.file_name"] = new()
        {
            ["en"] = "File name",
            ["es"] = "Nombre de archivo",
            ["it"] = "Nome del file",
        },
        ["pbi.schedule_files_one"] = new()
        {
            ["en"] = "File that will be written",
            ["es"] = "Archivo que se escribirá",
            ["it"] = "File che verrà scritto",
        },
        ["pbi.schedule_files_many"] = new()
        {
            ["en"] = "Files that will be written ({0}, one per schedule)",
            ["es"] = "Archivos que se escribirán ({0}, uno por tabla)",
            ["it"] = "File che verranno scritti ({0}, uno per abaco)",
        },
        ["pbi.schedule_files_note"] = new()
        {
            ["en"] = "The names come from each schedule, and the files are replaced on every export.",
            ["es"] = "Los nombres salen de cada tabla y los archivos se reemplazan en cada exportación.",
            ["it"] = "I nomi derivano da ogni abaco e i file vengono sostituiti a ogni esportazione.",
        },
        // Step 2: keeping it up to date, Power BI
        ["pbi.update"] = new()
        {
            ["en"] = "Updates",
            ["es"] = "Actualización",
            ["it"] = "Aggiornamento",
        },
        ["pbi.update_help"] = new()
        {
            ["en"] = "Keep the CSV current without exporting by hand.",
            ["es"] = "Mantenga el CSV al día sin exportar a mano.",
            ["it"] = "Mantieni il CSV aggiornato senza esportare a mano.",
        },
        ["pbi.overwrite"] = new()
        {
            ["en"] = "Overwrite the existing file",
            ["es"] = "Sobrescribir el archivo existente",
            ["it"] = "Sovrascrivi il file esistente",
        },
        ["pbi.overwrite_help"] = new()
        {
            ["en"] = "Recommended when Power BI refreshes on a schedule. Off: each export gets the date in its name.",
            ["es"] = "Recomendado si Power BI actualiza de forma programada. Apagado: cada exportación lleva la fecha en el nombre.",
            ["it"] = "Consigliato se Power BI si aggiorna in modo pianificato. Disattivato: ogni esportazione ha la data nel nome.",
        },
        ["pbi.auto_export"] = new()
        {
            ["en"] = "Export when the model is saved",
            ["es"] = "Exportar al guardar el modelo",
            ["it"] = "Esporta al salvataggio del modello",
        },
        ["pbi.auto_export_help"] = new()
        {
            ["en"] = "The CSV is exported again on every save, for this Revit session and while Vortex is on.",
            ["es"] = "El CSV se vuelve a exportar cada vez que guarda, en esta sesión de Revit y con Vortex encendido.",
            ["it"] = "Il CSV viene riesportato a ogni salvataggio, in questa sessione di Revit e con Vortex acceso.",
        },
        ["pbi.powerbi"] = new()
        {
            ["en"] = "Power BI",
            ["es"] = "Power BI",
            ["it"] = "Power BI",
        },
        ["pbi.powerbi_help"] = new()
        {
            ["en"] = "The link with Power BI Desktop and the Power BI service.",
            ["es"] = "Conexión con Power BI Desktop y el servicio Power BI.",
            ["it"] = "Collegamento con Power BI Desktop e il servizio Power BI.",
        },
        ["pbi.select_in_revit"] = new()
        {
            ["en"] = "Select in Revit from Power BI",
            ["es"] = "Seleccionar en Revit desde Power BI",
            ["it"] = "Seleziona in Revit da Power BI",
        },
        ["pbi.select_in_revit_help"] = new()
        {
            ["en"] = "Registers the revitcortex:// protocol on this computer.",
            ["es"] = "Registra el protocolo revitcortex:// en este equipo.",
            ["it"] = "Registra il protocollo revitcortex:// su questo computer.",
        },
        ["pbi.refresh"] = new()
        {
            ["en"] = "Refresh the dashboard in the Power BI service",
            ["es"] = "Actualizar el panel en Power BI Service",
            ["it"] = "Aggiorna la dashboard in Power BI Service",
        },
        ["pbi.refresh_help"] = new()
        {
            ["en"] = "Starts the dataset refresh after exporting.",
            ["es"] = "Lanza la actualización del dataset después de exportar.",
            ["it"] = "Avvia l'aggiornamento del dataset dopo l'esportazione.",
        },
        ["pbi.refresh_ids_help"] = new()
        {
            ["en"] = "Both are in the dataset's address: app.powerbi.com/groups/<workspace>/datasets/<dataset>.",
            ["es"] = "Los dos están en la dirección del dataset: app.powerbi.com/groups/<workspace>/datasets/<dataset>.",
            ["it"] = "Entrambi sono nell'indirizzo del dataset: app.powerbi.com/groups/<workspace>/datasets/<dataset>.",
        },
        ["pbi.workspace_tip"] = new()
        {
            ["en"] = "GUID of the Power BI workspace",
            ["es"] = "GUID del workspace de Power BI",
            ["it"] = "GUID del workspace di Power BI",
        },
        ["pbi.dataset_tip"] = new()
        {
            ["en"] = "GUID of the dataset the CSV feeds",
            ["es"] = "GUID del dataset que lee el CSV",
            ["it"] = "GUID del dataset alimentato dal CSV",
        },
        // Step 2: preview
        ["pbi.preview"] = new()
        {
            ["en"] = "Preview",
            ["es"] = "Vista previa",
            ["it"] = "Anteprima",
        },
        ["pbi.preview.first_rows"] = new()
        {
            ["en"] = "First {0} rows",
            ["es"] = "Primeras {0} filas",
            ["it"] = "Prime {0} righe",
        },
        ["pbi.preview.first_rows_of"] = new()
        {
            ["en"] = "First {0} rows of {1}",
            ["es"] = "Primeras {0} filas de {1}",
            ["it"] = "Prime {0} righe di {1}",
        },
        ["pbi.preview.summary"] = new()
        {
            ["en"] = "About {0} rows · {1} columns · {2}",
            ["es"] = "Unas {0} filas · {1} columnas · {2}",
            ["it"] = "Circa {0} righe · {1} colonne · {2}",
        },
        ["pbi.preview.more_columns"] = new()
        {
            ["en"] = "The file also carries UniqueId, DocumentTitle, DocumentPath and EpisodeId.",
            ["es"] = "El archivo lleva además UniqueId, DocumentTitle, DocumentPath y EpisodeId.",
            ["it"] = "Il file contiene anche UniqueId, DocumentTitle, DocumentPath ed EpisodeId.",
        },
        ["pbi.preview.skipped_one"] = new()
        {
            ["en"] = "{0} parameter is left out: a fixed column has the same name.",
            ["es"] = "{0} parámetro queda fuera: una columna fija tiene el mismo nombre.",
            ["it"] = "{0} parametro è escluso: una colonna fissa ha lo stesso nome.",
        },
        ["pbi.preview.skipped_many"] = new()
        {
            ["en"] = "{0} parameters are left out: fixed columns have the same names.",
            ["es"] = "{0} parámetros quedan fuera: hay columnas fijas con el mismo nombre.",
            ["it"] = "{0} parametri sono esclusi: colonne fisse hanno lo stesso nome.",
        },
        ["pbi.preview.capped"] = new()
        {
            ["en"] = "The export stops at {0} rows.",
            ["es"] = "La exportación se detiene en {0} filas.",
            ["it"] = "L'esportazione si ferma a {0} righe.",
        },
        ["pbi.preview.first_schedule_only"] = new()
        {
            ["en"] = "Showing the first schedule; each one gets its own file.",
            ["es"] = "Se muestra la primera tabla; cada una tiene su propio archivo.",
            ["it"] = "È mostrato il primo abaco; ognuno ha il proprio file.",
        },
        ["pbi.preview.no_rows"] = new()
        {
            ["en"] = "No rows to show.",
            ["es"] = "No hay filas para mostrar.",
            ["it"] = "Nessuna riga da mostrare.",
        },
        ["pbi.preview.nothing"] = new()
        {
            ["en"] = "Nothing to preview.",
            ["es"] = "No hay nada para previsualizar.",
            ["it"] = "Niente da mostrare in anteprima.",
        },
        // Step 2: column types (advanced)
        ["pbi.advanced"] = new()
        {
            ["en"] = "Advanced: column types",
            ["es"] = "Avanzado: tipos de columna",
            ["it"] = "Avanzate: tipi di colonna",
        },
        ["pbi.advanced_help"] = new()
        {
            ["en"] = "Power BI guesses each column's type, and the guess can change with the language, with empty first rows or with dates. Setting the types here keeps them the same on every refresh: the export adds a .pq file next to the CSV, and a _Raw column for each number.",
            ["es"] = "Power BI deduce el tipo de cada columna, y esa deducción cambia según el idioma, las primeras filas vacías o las fechas. Fijar los tipos aquí los mantiene iguales en cada actualización: la exportación agrega un archivo .pq junto al CSV y una columna _Raw por cada número.",
            ["it"] = "Power BI deduce il tipo di ogni colonna, e la deduzione cambia con la lingua, con le prime righe vuote o con le date. Impostare i tipi qui li mantiene uguali a ogni aggiornamento: l'esportazione aggiunge un file .pq accanto al CSV e una colonna _Raw per ogni numero.",
        },
        ["pbi.schema.auto"] = new()
        {
            ["en"] = "Automatic",
            ["es"] = "Automático",
            ["it"] = "Automatico",
        },
        ["pbi.schema.suggested"] = new()
        {
            ["en"] = "Suggested",
            ["es"] = "Sugerido",
            ["it"] = "Suggerito",
        },
        ["pbi.schema.custom"] = new()
        {
            ["en"] = "Custom",
            ["es"] = "Personalizado",
            ["it"] = "Personalizzato",
        },
        ["pbi.schema.column"] = new()
        {
            ["en"] = "Column",
            ["es"] = "Columna",
            ["it"] = "Colonna",
        },
        ["pbi.schema.type"] = new()
        {
            ["en"] = "Type in Power BI",
            ["es"] = "Tipo en Power BI",
            ["it"] = "Tipo in Power BI",
        },
        ["pbi.schema.format"] = new()
        {
            ["en"] = "Format",
            ["es"] = "Formato",
            ["it"] = "Formato",
        },
        ["pbi.schema.suggest"] = new()
        {
            ["en"] = "Suggest types from the parameters",
            ["es"] = "Sugerir tipos a partir de los parámetros",
            ["it"] = "Suggerisci i tipi dai parametri",
        },
        // Profiles
        ["pbi.profiles"] = new()
        {
            ["en"] = "Profiles",
            ["es"] = "Perfiles",
            ["it"] = "Profili",
        },
        ["pbi.profiles.load"] = new()
        {
            ["en"] = "Load a profile…",
            ["es"] = "Cargar perfil…",
            ["it"] = "Carica profilo…",
        },
        ["pbi.profiles.save"] = new()
        {
            ["en"] = "Save as profile…",
            ["es"] = "Guardar como perfil…",
            ["it"] = "Salva come profilo…",
        },
        ["pbi.profiles.import"] = new()
        {
            ["en"] = "Import from file…",
            ["es"] = "Importar desde archivo…",
            ["it"] = "Importa da file…",
        },
        ["pbi.profiles.open_folder"] = new()
        {
            ["en"] = "Open profiles folder",
            ["es"] = "Abrir carpeta de perfiles",
            ["it"] = "Apri cartella dei profili",
        },
        ["pbi.profiles.import_title"] = new()
        {
            ["en"] = "Import a profile (.json)",
            ["es"] = "Importar un perfil (.json)",
            ["it"] = "Importa un profilo (.json)",
        },
        ["pbi.profiles.import_filter"] = new()
        {
            ["en"] = "RVT Vortex profiles",
            ["es"] = "Perfiles de RVT Vortex",
            ["it"] = "Profili RVT Vortex",
        },
        ["pbi.profile_name.heading"] = new()
        {
            ["en"] = "Save as profile",
            ["es"] = "Guardar como perfil",
            ["it"] = "Salva come profilo",
        },
        ["pbi.profile_name.help"] = new()
        {
            ["en"] = "A profile keeps this export (data, columns and output) so you can load it again from Profiles.",
            ["es"] = "Un perfil guarda esta exportación (datos, columnas y salida) para volver a cargarla desde Perfiles.",
            ["it"] = "Un profilo conserva questa esportazione (dati, colonne e output) per ricaricarla da Profili.",
        },
        ["pbi.profile_name.label"] = new()
        {
            ["en"] = "Profile name",
            ["es"] = "Nombre del perfil",
            ["it"] = "Nome del profilo",
        },
        ["pbi.profile_picker.heading"] = new()
        {
            ["en"] = "Load a profile",
            ["es"] = "Cargar perfil",
            ["it"] = "Carica profilo",
        },
        ["pbi.profile_picker.help"] = new()
        {
            ["en"] = "The saved exports, most recent first.",
            ["es"] = "Las exportaciones guardadas, la más reciente primero.",
            ["it"] = "Le esportazioni salvate, dalla più recente.",
        },
        ["pbi.profile_picker.empty"] = new()
        {
            ["en"] = "No saved profiles.",
            ["es"] = "No hay perfiles guardados.",
            ["it"] = "Nessun profilo salvato.",
        },
        ["pbi.profile_picker.delete"] = new()
        {
            ["en"] = "Delete",
            ["es"] = "Eliminar",
            ["it"] = "Elimina",
        },
        ["pbi.profile_picker.load"] = new()
        {
            ["en"] = "Load",
            ["es"] = "Cargar",
            ["it"] = "Carica",
        },
        // Footer messages
        ["pbi.status.categories_failed"] = new()
        {
            ["en"] = "The model's categories could not be read: {0}",
            ["es"] = "No se pudieron leer las categorías del modelo: {0}",
            ["it"] = "Impossibile leggere le categorie del modello: {0}",
        },
        ["pbi.status.no_categories"] = new()
        {
            ["en"] = "The active model has no categories with elements.",
            ["es"] = "El modelo activo no tiene categorías con elementos.",
            ["it"] = "Il modello attivo non ha categorie con elementi.",
        },
        ["pbi.status.selection_empty"] = new()
        {
            ["en"] = "Nothing is selected in Revit. Select elements in the model, then choose Current selection again.",
            ["es"] = "No hay nada seleccionado en Revit. Seleccione elementos en el modelo y vuelva a elegir Selección actual.",
            ["it"] = "Nessun elemento selezionato in Revit. Seleziona degli elementi nel modello e scegli di nuovo Selezione corrente.",
        },
        ["pbi.status.parameters_failed"] = new()
        {
            ["en"] = "The parameters could not be read: {0}",
            ["es"] = "No se pudieron leer los parámetros: {0}",
            ["it"] = "Impossibile leggere i parametri: {0}",
        },
        ["pbi.status.preview_failed"] = new()
        {
            ["en"] = "The preview could not be built: {0}",
            ["es"] = "No se pudo armar la vista previa: {0}",
            ["it"] = "Impossibile creare l'anteprima: {0}",
        },
        ["pbi.status.no_profiles"] = new()
        {
            ["en"] = "No saved profiles yet. Set up an export and keep it with Save as profile.",
            ["es"] = "Todavía no hay perfiles guardados. Arme una exportación y guárdela con Guardar como perfil.",
            ["it"] = "Nessun profilo salvato. Configura un'esportazione e conservala con Salva come profilo.",
        },
        ["pbi.status.profile_loaded"] = new()
        {
            ["en"] = "Profile “{0}” loaded.",
            ["es"] = "Perfil «{0}» cargado.",
            ["it"] = "Profilo «{0}» caricato.",
        },
        ["pbi.status.profile_saved"] = new()
        {
            ["en"] = "Profile “{0}” saved in {1}",
            ["es"] = "Perfil «{0}» guardado en {1}",
            ["it"] = "Profilo «{0}» salvato in {1}",
        },
        ["pbi.status.profile_save_failed"] = new()
        {
            ["en"] = "The profile could not be saved: {0}",
            ["es"] = "No se pudo guardar el perfil: {0}",
            ["it"] = "Impossibile salvare il profilo: {0}",
        },
        ["pbi.status.profile_invalid"] = new()
        {
            ["en"] = "That file is not a profile, or it has no name.",
            ["es"] = "Ese archivo no es un perfil o no tiene nombre.",
            ["it"] = "Quel file non è un profilo o non ha un nome.",
        },
        ["pbi.status.profile_imported"] = new()
        {
            ["en"] = "Profile “{0}” imported and applied.",
            ["es"] = "Perfil «{0}» importado y aplicado.",
            ["it"] = "Profilo «{0}» importato e applicato.",
        },
        ["pbi.status.profile_import_failed"] = new()
        {
            ["en"] = "The profile could not be imported: {0}",
            ["es"] = "No se pudo importar el perfil: {0}",
            ["it"] = "Impossibile importare il profilo: {0}",
        },
        ["pbi.status.open_folder_failed"] = new()
        {
            ["en"] = "The folder could not be opened: {0}",
            ["es"] = "No se pudo abrir la carpeta: {0}",
            ["it"] = "Impossibile aprire la cartella: {0}",
        },
        ["pbi.status.need_folder"] = new()
        {
            ["en"] = "Set an output folder first.",
            ["es"] = "Primero indique una carpeta de salida.",
            ["it"] = "Imposta prima una cartella di output.",
        },
        ["pbi.status.exporting"] = new()
        {
            ["en"] = "Exporting…",
            ["es"] = "Exportando…",
            ["it"] = "Esportazione in corso…",
        },
        ["pbi.status.no_router"] = new()
        {
            ["en"] = "RVT Vortex did not finish starting. Restart Revit and try again.",
            ["es"] = "RVT Vortex no terminó de iniciar. Reinicie Revit y vuelva a intentar.",
            ["it"] = "RVT Vortex non ha completato l'avvio. Riavvia Revit e riprova.",
        },
        ["pbi.status.no_response"] = new()
        {
            ["en"] = "The export gave no answer.",
            ["es"] = "La exportación no devolvió respuesta.",
            ["it"] = "L'esportazione non ha dato risposta.",
        },
        ["pbi.status.export_failed"] = new()
        {
            ["en"] = "The export failed ({0}): {1}",
            ["es"] = "La exportación falló ({0}): {1}",
            ["it"] = "Esportazione non riuscita ({0}): {1}",
        },
        ["pbi.status.exported"] = new()
        {
            ["en"] = "Export finished: {0} rows → {1}",
            ["es"] = "Exportación terminada: {0} filas → {1}",
            ["it"] = "Esportazione completata: {0} righe → {1}",
        },
        ["pbi.status.unexpected"] = new()
        {
            ["en"] = "Unexpected error ({0}): {1}",
            ["es"] = "Error inesperado ({0}): {1}",
            ["it"] = "Errore imprevisto ({0}): {1}",
        },
        ["pbi.status.refresh_starting"] = new()
        {
            ["en"] = "Export finished. Asking Power BI to refresh…",
            ["es"] = "Exportación terminada. Pidiendo a Power BI que actualice…",
            ["it"] = "Esportazione completata. Richiesta di aggiornamento a Power BI…",
        },
        ["pbi.status.refresh_not_signed_in"] = new()
        {
            ["en"] = "Export finished. The refresh was not started: you are not signed in to Power BI (ask the AI to run pbi_check_auth).",
            ["es"] = "Exportación terminada. No se lanzó la actualización: no inició sesión en Power BI (pídale a la IA que ejecute pbi_check_auth).",
            ["it"] = "Esportazione completata. Aggiornamento non avviato: non hai effettuato l'accesso a Power BI (chiedi all'IA di eseguire pbi_check_auth).",
        },
        ["pbi.status.refresh_queued"] = new()
        {
            ["en"] = "Export finished. Refresh queued; the dashboard updates in about 30 seconds.",
            ["es"] = "Exportación terminada. Actualización en cola; el panel se actualiza en unos 30 segundos.",
            ["it"] = "Esportazione completata. Aggiornamento in coda; la dashboard si aggiorna in circa 30 secondi.",
        },
        ["pbi.status.refresh_started"] = new()
        {
            ["en"] = "Export finished. Refresh started (request {0}); the dashboard updates in about 30 seconds.",
            ["es"] = "Exportación terminada. Actualización iniciada (solicitud {0}); el panel se actualiza en unos 30 segundos.",
            ["it"] = "Esportazione completata. Aggiornamento avviato (richiesta {0}); la dashboard si aggiorna in circa 30 secondi.",
        },
        ["pbi.status.refresh_failed"] = new()
        {
            ["en"] = "Export finished. The refresh failed: {0}",
            ["es"] = "Exportación terminada. La actualización falló: {0}",
            ["it"] = "Esportazione completata. Aggiornamento non riuscito: {0}",
        },
        ["pbi.unknown_error"] = new()
        {
            ["en"] = "unknown error",
            ["es"] = "error desconocido",
            ["it"] = "errore sconosciuto",
        },
        ["pbi.unknown_path"] = new()
        {
            ["en"] = "(unknown path)",
            ["es"] = "(ruta desconocida)",
            ["it"] = "(percorso sconosciuto)",
        },
        // Revit dialogs
        ["pbi.dialog.categories_failed"] = new()
        {
            ["en"] = "Power BI export: the categories could not be read",
            ["es"] = "Exportar a Power BI: no se pudieron leer las categorías",
            ["it"] = "Esporta in Power BI: impossibile leggere le categorie",
        },
        ["pbi.dialog.export_failed"] = new()
        {
            ["en"] = "Power BI export failed ({0})",
            ["es"] = "La exportación a Power BI falló ({0})",
            ["it"] = "Esportazione in Power BI non riuscita ({0})",
        },
        ["pbi.dialog.unexpected"] = new()
        {
            ["en"] = "Power BI export: unexpected error",
            ["es"] = "Exportar a Power BI: error inesperado",
            ["it"] = "Esporta in Power BI: errore imprevisto",
        },
        ["pbi.dialog.done_title"] = new()
        {
            ["en"] = "Power BI export finished",
            ["es"] = "Exportación a Power BI terminada",
            ["it"] = "Esportazione in Power BI completata",
        },
        ["pbi.dialog.exported_rows"] = new()
        {
            ["en"] = "{0} rows exported",
            ["es"] = "Se exportaron {0} filas",
            ["it"] = "Esportate {0} righe",
        },
        ["pbi.dialog.exported_schedules_one"] = new()
        {
            ["en"] = "{0} schedule exported",
            ["es"] = "Se exportó {0} tabla de planificación",
            ["it"] = "Esportato {0} abaco",
        },
        ["pbi.dialog.exported_schedules_many"] = new()
        {
            ["en"] = "{0} schedules exported",
            ["es"] = "Se exportaron {0} tablas de planificación",
            ["it"] = "Esportati {0} abachi",
        },
        ["pbi.dialog.file"] = new()
        {
            ["en"] = "File: {0}",
            ["es"] = "Archivo: {0}",
            ["it"] = "File: {0}",
        },
        ["pbi.dialog.files_in"] = new()
        {
            ["en"] = "Files written to: {0}",
            ["es"] = "Archivos escritos en: {0}",
            ["it"] = "File scritti in: {0}",
        },
        ["pbi.dialog.open_folder"] = new()
        {
            ["en"] = "Open the folder",
            ["es"] = "Abrir la carpeta",
            ["it"] = "Apri la cartella",
        },
        ["pbi.dialog.open_folder_hint"] = new()
        {
            ["en"] = "Shows the output folder in File Explorer",
            ["es"] = "Muestra la carpeta de salida en el Explorador",
            ["it"] = "Mostra la cartella di output in Esplora file",
        },
    };
}
