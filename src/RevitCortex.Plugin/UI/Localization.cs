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
            ["en"] = "RevitCortex Premium",
            ["it"] = "RevitCortex Premium",
            ["es"] = "RevitCortex Premium",
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
            ["en"] = "RevitCortex Premium {0} is available",
            ["it"] = "È disponibile RevitCortex Premium {0}",
            ["es"] = "Está disponible RevitCortex Premium {0}",
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
            ["en"] = "When a command fails, RevitCortex Premium can send an anonymous error report: tool name, error type, versions, timing. Never sent: model names, file paths, parameter values, user or machine names.\n\nConsent is optional and you can change it anytime in Settings > General.",
            ["it"] = "Quando un comando fallisce, RevitCortex Premium può inviare una segnalazione di errore anonima: nome del tool, tipo di errore, versioni, tempi. Mai inviati: nomi dei modelli, percorsi file, valori dei parametri, nomi utente o macchina.\n\nIl consenso è facoltativo e puoi modificarlo in qualsiasi momento da Impostazioni > Generale.",
            ["es"] = "Cuando un comando falla, RevitCortex Premium puede enviar un informe de error anónimo: nombre de la herramienta, tipo de error, versiones y tiempos. Nunca se envían nombres de modelos, rutas de archivos, valores de parámetros ni nombres de usuario o equipo.\n\nEl consentimiento es opcional y puede cambiarlo cuando quiera en Configuración > General.",
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

        // ── Fork: ribbon, dialogs and Autopilot (en / es / it) ─────────
        ["ribbon.connect.tooltip_off"] = new()
        {
            ["en"] = "Start the RevitCortex server so the AI can work in this model",
            ["es"] = "Iniciar el servidor de RevitCortex para que la IA pueda trabajar en este modelo",
            ["it"] = "Avvia il server RevitCortex perché l'IA possa lavorare su questo modello",
        },
        ["ribbon.connect.tooltip_on"] = new()
        {
            ["en"] = "RevitCortex server running on port {0} — click to stop",
            ["es"] = "Servidor de RevitCortex activo en el puerto {0} — clic para detenerlo",
            ["it"] = "Server RevitCortex attivo sulla porta {0} — clic per fermarlo",
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
            ["en"] = "RevitCortex settings",
            ["es"] = "Configuración de RevitCortex",
            ["it"] = "Impostazioni di RevitCortex",
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
            ["en"] = "Send log\r\nto support",
            ["es"] = "Enviar\r\nregistros",
            ["it"] = "Invia log\r\nal supporto",
        },
        ["ribbon.support.tooltip"] = new()
        {
            ["en"] = "Send a bug report to RevitCortex support",
            ["es"] = "Enviar un informe de error al soporte de RevitCortex",
            ["it"] = "Invia una segnalazione al supporto di RevitCortex",
        },
        ["ribbon.support.long"] = new()
        {
            ["en"] = "Collects recent audit logs, token-usage log, settings and the most recent Revit journal into a ZIP on the desktop, then opens a pre-filled Outlook message addressed to support. Add a short description of the problem and click Send. No personal data is sent beyond what's in the logs.",
            ["es"] = "Reúne los registros de auditoría recientes, el registro de uso de tokens, la configuración y el último journal de Revit en un ZIP en el escritorio, y abre un correo de Outlook dirigido a soporte. Agregue una breve descripción del problema y haga clic en Enviar. No se envían datos personales más allá de lo que contienen los registros.",
            ["it"] = "Raccoglie i log di audit recenti, il log di utilizzo token, le impostazioni e il journal di Revit più recente in uno ZIP sul desktop, poi apre un messaggio Outlook precompilato per il supporto. Aggiungi una breve descrizione del problema e clicca Invia. Non vengono inviati dati personali oltre a quelli contenuti nei log.",
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
            ["en"] = "RevitCortex — Confirmation",
            ["es"] = "RevitCortex — Confirmación",
            ["it"] = "RevitCortex — Conferma",
        },
        ["confirm.critical_title"] = new()
        {
            ["en"] = "RevitCortex — Critical confirmation",
            ["es"] = "RevitCortex — Confirmación crítica",
            ["it"] = "RevitCortex — Conferma critica",
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
            ["en"] = "RevitCortex — Autopilot",
            ["es"] = "RevitCortex — Piloto automático",
            ["it"] = "RevitCortex — Pilota automatico",
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
            ["en"] = "The server (Cortex Switch) is off. Turn it on so the AI can work.",
            ["es"] = "El servidor (Cortex Switch) está apagado. Enciéndalo para que la IA pueda trabajar.",
            ["it"] = "Il server (Cortex Switch) è spento. Accendilo perché l'IA possa lavorare.",
        },
        ["ap.footer_unsaved"] = new()
        {
            ["en"] = "This model has never been saved, so it cannot be saved automatically. Save it once before you leave.",
            ["es"] = "Este modelo nunca se guardó, así que no se puede guardar automáticamente. Guárdelo una vez antes de irse.",
            ["it"] = "Questo modello non è mai stato salvato, quindi non può essere salvato automaticamente. Salvalo una volta prima di allontanarti.",
        },
        ["win.title"] = new()
        {
            ["en"] = "RevitCortex",
            ["es"] = "RevitCortex",
            ["it"] = "RevitCortex",
        },
        ["win.auto_heading"] = new()
        {
            ["en"] = "Auto mode ON",
            ["es"] = "Modo automático ACTIVO",
            ["it"] = "Modalità automatica ATTIVA",
        },
        ["win.auto_detail"] = new()
        {
            ["en"] = "Edits are approved automatically, without confirmation dialogs.",
            ["es"] = "Las modificaciones se aprueban automáticamente, sin cuadros de confirmación.",
            ["it"] = "Le modifiche vengono approvate automaticamente, senza finestre di conferma.",
        },
        ["win.autopilot_heading"] = new()
        {
            ["en"] = "Autopilot ON",
            ["es"] = "Piloto automático ACTIVO",
            ["it"] = "Pilota automatico ATTIVO",
        },
        ["win.autopilot_detail"] = new()
        {
            ["en"] = "The AI keeps working without dialogs. The model is saved after changes and everything is logged.",
            ["es"] = "La IA sigue trabajando sin cuadros de diálogo. El modelo se guarda después de los cambios y todo queda registrado.",
            ["it"] = "L'IA continua a lavorare senza finestre di dialogo. Il modello viene salvato dopo le modifiche e tutto viene registrato.",
        },
        ["win.stop"] = new()
        {
            ["en"] = "Stop",
            ["es"] = "Detener",
            ["it"] = "Ferma",
        },
        ["win.stop_desc"] = new()
        {
            ["en"] = "Turn it off and bring back confirmation dialogs",
            ["es"] = "Apagarlo y volver a mostrar los cuadros de confirmación",
            ["it"] = "Spegnilo e ripristina le finestre di conferma",
        },
        ["win.status_idle"] = new()
        {
            ["en"] = "Waiting for the AI…",
            ["es"] = "Esperando a la IA…",
            ["it"] = "In attesa dell'IA…",
        },
        ["win.status_approved"] = new()
        {
            ["en"] = "{0} · approved: {1} ({2})",
            ["es"] = "{0} · aprobado: {1} ({2})",
            ["it"] = "{0} · approvato: {1} ({2})",
        },
        ["win.status_declined"] = new()
        {
            ["en"] = "{0} · declined: {1} ({2})",
            ["es"] = "{0} · rechazado: {1} ({2})",
            ["it"] = "{0} · rifiutato: {1} ({2})",
        },
        ["win.status_saved"] = new()
        {
            ["en"] = "{0} · model saved",
            ["es"] = "{0} · modelo guardado",
            ["it"] = "{0} · modello salvato",
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
    };
}
