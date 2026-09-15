using System.Globalization;
namespace Entriqa.Admin.Services;

/// <summary>
/// Interface language of the admin. German is the source language and at the same time the key:
/// <c>T["Speichern"]</c> returns the key itself in German and the translation in English -
/// a missing entry safely falls back to German. The choice lives in localStorage;
/// switching reloads the app so that every page changes over consistently.
/// </summary>
public sealed class Ui
{
    public const string StorageKey = "entriqa-admin-lang";

    public string Lang { get; private set; } = "de";

    public void Init(string? stored)
    {
        if (stored == "en") Lang = "en";
    }

    public string this[string text] => Lang == "de" ? text : En.GetValueOrDefault(text, text);

    public string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, this[format], args);

    private static readonly Dictionary<string, string> En = new()
    {
        // Navigation & Rahmen
        ["Formulare"] = "Forms",
        ["Einsendungen"] = "Submissions",
        ["Auswertungen"] = "Insights",
        ["Einstellungen"] = "Settings",
        ["neu"] = "new",
        ["Abmelden"] = "Sign out",
        ["Anmelden"] = "Sign in",
        ["Lade …"] = "Loading …",
        ["Lade Admin …"] = "Loading admin …",

        // Gemeinsame Aktionen
        ["Speichern"] = "Save",
        ["Prüfen"] = "Check",
        ["Hinzufügen"] = "Add",
        ["Entfernen"] = "Remove",
        ["Abbrechen"] = "Cancel",
        ["Kopieren"] = "Copy",
        ["Schließen"] = "Close",
        ["Bearbeiten"] = "Edit",
        ["Wiederholen"] = "Retry",
        ["Nach oben"] = "Move up",
        ["Nach unten"] = "Move down",
        ["▲ Hoch"] = "▲ Up",
        ["▼ Runter"] = "▼ Down",

        // Formularliste
        ["Typ"] = "Type",
        ["Status"] = "Status",
        ["Version"] = "Version",
        ["Einsendungen · 14 Tage"] = "Submissions · 14 days",
        ["Geändert"] = "Changed",
        ["Veröffentlicht"] = "Published",
        ["Entwurf"] = "Draft",
        ["{0} in 7 Tagen"] = "{0} in 7 days",
        ["Einbetten"] = "Embed",
        ["Name des neuen Formulars"] = "Name of the new form",
        ["Kontakt"] = "Contact",
        ["Lead-Magnet"] = "Lead magnet",
        ["Quiz"] = "Quiz",
        ["Anlegen"] = "Create",
        ["Slug existiert schon."] = "Slug already exists.",
        ["Suchen (Slug, Name)"] = "Search (slug, name)",
        ["{0} Formulare"] = "{0} forms",
        ["Seite {0} von {1}"] = "Page {0} of {1}",
        ["Aus JSON importieren …"] = "Import from JSON …",
        ["Formular aus JSON importieren"] = "Import form from JSON",
        ["Definitions-JSON einfügen (z. B. aus dem Expertenmodus einer anderen Instanz kopiert). Es entsteht ein Entwurf unter dem Slug aus dem JSON – veröffentlicht wird erst nach deiner Prüfung."] =
            "Paste a definition JSON (e.g. copied from another instance's expert mode). This creates a draft under the slug from the JSON – nothing is published until you review it.",
        ["Importieren"] = "Import",
        ["Das JSON hat keinen 'slug'."] = "The JSON has no 'slug'.",
        ["Slug '{0}' existiert schon – im JSON umbenennen."] = "Slug '{0}' already exists – rename it in the JSON.",
        ["JSON kopieren"] = "Copy JSON",
        ["Einbetten: {0}"] = "Embed: {0}",
        ["Shortcode in eine Hugo-Seite einfügen oder die eigenständige Adresse teilen. Beides zeigt immer die zuletzt veröffentlichte Version."] =
            "Insert the shortcode into a Hugo page or share the standalone address. Both always show the latest published version.",

        // Editor: Kopf & Tabs
        ["Veröffentlicht (v{0})"] = "Published (v{0})",
        ["zuletzt geändert von {0}"] = "last changed by {0}",
        ["Offen/Erledigt führen"] = "Track open/done",
        ["Sprachen"] = "Languages",
        ["Felder & Texte"] = "Fields & copy",
        ["Fragen & Weichen"] = "Questions & branching",
        ["Verarbeitung"] = "Processing",
        ["Rückmeldung"] = "Response",
        ["Markup & Klassen"] = "Markup & classes",
        ["Expertenmodus"] = "Expert mode",
        ["Markup- und JSON-Ansicht ein-/ausblenden"] = "Show/hide markup and JSON views",
        ["Neues Formular – Vorlage geladen, noch nicht gespeichert."] = "New form – template loaded, not saved yet.",
        ["automatisch gespeichert {0}"] = "auto-saved {0}",

        // Editor: Builder
        ["Struktur"] = "Structure",
        ["Einleitung & Button"] = "Intro & button",
        ["+ Feld hinzufügen"] = "+ Add field",
        ["Zum Bearbeiten anklicken · Gestaltung kommt vom Website-Theme"] = "Click to edit · styling comes from the website theme",
        ["Texte"] = "Copy",
        ["Einleitungstext"] = "Intro text",
        ["Absende-Button"] = "Submit button",
        ["Felder links oder direkt in der Vorschau anklicken, um sie zu bearbeiten."] = "Click a field on the left or directly in the preview to edit it.",
        ["Feld-Eigenschaften"] = "Field properties",
        ["Einleitungstext …"] = "Intro text …",
        ["Verstecktes Feld: {0} ({1})"] = "Hidden field: {0} ({1})",
        ["ohne Namen"] = "unnamed",
        ["Trennlinie"] = "Divider",

        // Editor: Quiz-Tab
        ["Dieses Formular hat noch kein Quiz."] = "This form has no quiz yet.",
        ["Quiz anlegen"] = "Create quiz",

        // editor: processing
        ["Einsendung geht ein"] = "Submission arrives",
        ["Nach erfolgreicher Prüfung laufen die Schritte von oben nach unten."] = "After validation, the steps run from top to bottom.",
        ["wartet auf Bestätigung"] = "waiting for confirmation",
        ["Schritt hinzufügen …"] = "Add step …",

        // editor: feedback
        ["Nach dem Absenden"] = "After submitting",
        ["Meldung anzeigen"] = "Show a message",
        ["Weiterleiten"] = "Redirect",
        ["Quiz-Ergebnis anzeigen"] = "Show quiz result",
        ["Meldung"] = "Message",
        ["Ziel-URL (z. B. /danke/)"] = "Target URL (e.g. /thanks/)",
        ["Spam-Schutz ist immer aktiv: Honeypot, Mindestalter 3 Sekunden, Rate-Limit je Absender – ohne Cookies, ohne Captcha."] =
            "Spam protection is always on: honeypot, 3-second minimum age, per-sender rate limit – no cookies, no captcha.",

        // Editor: Markup & JSON
        ["So rendert forms.js dieses Formular – ohne Inline-Styles, ohne iframe. Gestaltung ausschließlich über das CSS der Website."] =
            "This is how forms.js renders the form – no inline styles, no iframe. Styling happens entirely in the website's CSS.",
        ["Klassen-Referenz (stabiler Vertrag fürs Theme)"] = "Class reference (stable contract for the theme)",
        ["Expertenansicht: das vollständige Definitions-JSON. Änderungen fließen beim Tab-Wechsel in den Builder zurück."] =
            "Expert view: the complete definition JSON. Changes flow back into the builder when you switch tabs.",
        ["Kein gültiges JSON – bitte erst korrigieren: {0}"] = "Invalid JSON – please fix it first: {0}",
        ["Kein gültiges JSON: {0}"] = "Invalid JSON: {0}",

        // editor: check and publish
        ["Keine Probleme gefunden – bereit zum Veröffentlichen."] = "No issues found – ready to publish.",
        ["Veröffentlichen …"] = "Publish …",
        ["Entwurf gespeichert."] = "Draft saved.",
        ["Noch nicht bereit – bitte erst die Probleme beheben."] = "Not ready yet – please fix the issues first.",
        ["Nicht veröffentlicht – bitte die Probleme beheben."] = "Not published – please fix the issues.",
        ["Version {0} veröffentlichen"] = "Publish version {0}",
        ["Die aktuelle Definition wird eingefroren."] = "The current definition will be frozen.",
        ["Bestehende Einsendungen bleiben Version {0} zugeordnet."] = "Existing submissions stay assigned to version {0}.",
        ["Danach ist das Formular öffentlich erreichbar."] = "Afterwards the form is publicly available.",
        ["Felder"] = "Fields",
        ["{0} Fragen, {1} Ergebnisse"] = "{0} questions, {1} results",
        ["– nur speichern –"] = "– store only –",
        ["Adresse"] = "Address",
        ["Jetzt veröffentlichen"] = "Publish now",
        ["Veröffentlicht als Version {0}"] = "Published as version {0}",

        // field editor
        ["z. B. nachricht"] = "e.g. message",
        ["Interner Feldname – taucht in Auswertung und Export auf"] = "Internal field name – appears in insights and exports",
        ["Beschriftung"] = "Label",
        ["Überschrift"] = "Heading",
        ["Pflichtfeld"] = "Required field",
        ["Platzhalter (optional)"] = "Placeholder (optional)",
        ["Hilfetext (optional)"] = "Help text (optional)",
        ["Nur geschäftliche Adressen (Freemail ablehnen)"] = "Business addresses only (reject freemail)",
        ["Einwilligungstext (wird mit der Version archiviert)"] = "Consent text (archived with the version)",
        ["Optionen"] = "Options",
        ["Option entfernen"] = "Remove option",
        ["Option hinzufügen"] = "Add option",
        ["Maximale Länge"] = "Maximum length",
        ["Quelle"] = "Source",
        ["Verweisende Seite"] = "Referring page",
        ["Aktuelle Seite"] = "Current page",
        ["URL-Parameter …"] = "URL parameter …",
        ["Fester Wert …"] = "Fixed value …",
        ["Parametername"] = "Parameter name",
        ["Wert"] = "Value",

        // conditional visibility and new field types
        ["Nur anzeigen, wenn …"] = "Only show when …",
        ["– immer anzeigen –"] = "– always show –",
        ["… angehakt ist."] = "… is checked.",
        ["… ausgefüllt ist."] = "… has a value.",
        ["… eine dieser Antworten hat:"] = "… has one of these answers:",
        ["bedingt"] = "conditional",
        ["Wird nur unter einer Bedingung angezeigt"] = "Only shown under a condition",
        ["Seite"] = "Page",
        ["Stufen (2–10)"] = "Steps (2–10)",
        ["– Feld wählen –"] = "– choose field –",
        ["Firma in Brevo pflegen"] = "Maintain company in Brevo",
        ["Sucht die Firma im Brevo-CRM nach Namen, legt sie bei Bedarf an und verknüpft den Kontakt mit ihr."] =
            "Looks the company up in the Brevo CRM by name, creates it if needed, and links the contact to it.",
        ["Feld mit dem Firmennamen"] = "Field holding the company name",

        // Feldtypen
        ["Textzeile"] = "Text line",
        ["E-Mail"] = "Email",
        ["Mehrzeiliger Text"] = "Multi-line text",
        ["Zahl"] = "Number",
        ["Auswahl"] = "Select",
        ["Mehrfachauswahl"] = "Multi-select",
        ["Checkbox"] = "Checkbox",
        ["Datum"] = "Date",
        ["Einwilligung"] = "Consent",
        ["Verstecktes Feld"] = "Hidden field",
        ["Zwischenüberschrift"] = "Section heading",
        ["Telefon"] = "Phone",
        ["Bewertungsskala"] = "Rating scale",
        ["Seitenumbruch"] = "Page break",
        ["Datei-Upload"] = "File upload",
        ["Erlaubt: PDF, Word, Excel, PowerPoint, Text und Bilder (PNG, JPG, GIF, WebP) – höchstens 10 MB. Die Datei liegt privat; der Admin bekommt zeitlich begrenzte Download-Links."] =
            "Allowed: PDF, Word, Excel, PowerPoint, text, and images (PNG, JPG, GIF, WebP) – at most 10 MB. Files are stored privately; the admin gets time-limited download links.",
        ["Herunterladen"] = "Download",

        // step editor
        ["Hintergrund"] = "Background",
        ["teilt: alles danach erst nach Bestätigung"] = "splits: everything after runs only once confirmed",
        ["Ausführen"] = "Run",
        ["immer"] = "always",
        ["nur mit E-Mail-Adresse"] = "only with an email address",
        ["nur bei Quiz-Ergebnis …"] = "only for quiz result …",
        ["– Ergebnis wählen –"] = "– choose result –",
        ["Bei Fehler"] = "On failure",
        ["Standard ({0})"] = "Default ({0})",
        ["weiterlaufen"] = "continue",
        ["anhalten"] = "stop",
        ["Folgeschritte anhalten"] = "Stop subsequent steps",
        ["Folgeschritte weiterlaufen lassen"] = "Let subsequent steps continue",
        ["– Vorlage wählen –"] = "– choose template –",
        ["– Datei wählen –"] = "– choose file –",
        ["lädt hoch …"] = "uploading …",
        ["Erst wählbar, wenn das Quiz Ergebnisse hat."] = "Only selectable once the quiz has results.",
        ["Kein vorheriger Schritt erzeugt etwas zum Mitschicken."] = "No previous step produces anything to attach.",
        ["nichts"] = "nothing",
        ["Download-Link"] = "Download link",
        ["PDF als Anhang"] = "PDF as attachment",
        ["PDF als Link"] = "PDF as link",
        ["Upload fehlgeschlagen: {0}"] = "Upload failed: {0}",

        // step catalog (server texts - mirrored here, unknown ones stay German)
        ["Kontakt in Brevo anlegen"] = "Create contact in Brevo",
        ["Legt den Kontakt an oder aktualisiert ihn und trägt ihn in die gewählten Listen ein."] = "Creates or updates the contact and adds it to the selected lists.",
        ["E-Mail an Teilnehmer"] = "Email to participant",
        ["Verschickt eine Brevo-Vorlage an die angegebene Adresse – optional mit Datei oder Link aus einem vorherigen Schritt."] = "Sends a Brevo template to the given address – optionally with a file or link from a previous step.",
        ["Bestätigung anfordern (Double-Opt-In)"] = "Request confirmation (double opt-in)",
        ["Verschickt eine neutrale Bestätigungsmail mit Link. Alles, was danach kommt, läuft erst nach dem Klick."] = "Sends a neutral confirmation email with a link. Everything after runs only once it is clicked.",
        ["Download-Link erzeugen"] = "Create download link",
        ["Erzeugt einen zeitlich begrenzten Link auf eine privat gespeicherte Datei."] = "Creates a time-limited link to a privately stored file.",
        ["Benachrichtigung an uns"] = "Notify us",
        ["Schickt die Einsendung per Brevo-Transaktionsmail an eine interne Adresse."] = "Sends the submission via Brevo transactional email to an internal address.",
        ["PDF erzeugen (ReportingCloud)"] = "Create PDF (ReportingCloud)",
        ["Füllt eine Word-Vorlage mit den Angaben und erzeugt ein PDF. Läuft im Hintergrund."] = "Fills a Word template with the answers and creates a PDF. Runs in the background.",
        ["Teams-Nachricht"] = "Teams message",
        ["Postet die Einsendung als Karte in einen Teams-Kanal (Workflows-Webhook-URL des Kanals)."] = "Posts the submission as a card into a Teams channel (the channel's Workflows webhook URL).",
        ["Webhook aufrufen"] = "Call webhook",
        ["Schickt die Einsendung als JSON an eine URL – z. B. an einen Power-Automate-Flow, der eine To-Do-Aufgabe anlegt."] = "Sends the submission as JSON to a URL – e.g. a Power Automate flow that creates a To-Do task.",
        ["Brevo-Listen"] = "Brevo lists",
        ["Suchen …"] = "Search …",
        ["Keine Treffer"] = "No matches",
        ["nichts gewählt"] = "nothing selected",
        ["nicht mehr in Brevo"] = "no longer in Brevo",
        ["Verzeichnis nicht geladen"] = "directory not loaded",
        ["Ids direkt eintragen (kommagetrennt)"] = "Enter ids directly (comma separated)",
        ["Id direkt eintragen"] = "Enter the id directly",
        ["Unbekannt (#{0})"] = "Unknown (#{0})",
        ["{0} entfernen"] = "Remove {0}",
        ["{0} weitere – Suche verfeinern"] = "{0} more – narrow the search",
        ["Das Verzeichnis konnte nicht vollständig geladen werden – hier fehlen möglicherweise Einträge."] =
            "The directory could not be loaded completely – entries may be missing here.",
        ["Brevo-Vorlage"] = "Brevo template",
        ["Mitschicken"] = "Attach",
        ["Link gültig (Stunden), nur bei reportLink"] = "Link valid (hours), reportLink only",
        ["Bestätigungsmail (Brevo-Vorlage)"] = "Confirmation email (Brevo template)",
        ["Datei (Blob-Pfad unter leadmagnets/)"] = "File (blob path under leadmagnets/)",
        ["Link gültig (Stunden)"] = "Link valid (hours)",
        ["E-Mail-Adresse(n), durch Komma getrennt"] = "Email address(es), comma-separated",
        ["Vorlage (ohne Quiz)"] = "Template (without quiz)",
        ["Vorlage je Quiz-Ergebnis"] = "Template per quiz result",
        ["Workflows-Webhook-URL des Kanals"] = "The channel's Workflows webhook URL",
        ["Kartentitel (Platzhalter wie beim Webhook)"] = "Card title (same placeholders as the webhook)",

        // Quiz-Editor
        ["E-Mail-Erfassung"] = "Email capture",
        ["kein Kontaktschritt"] = "no contact step",
        ["optional (nach der letzten Frage)"] = "optional (after the last question)",
        ["Pflicht (nach der letzten Frage)"] = "required (after the last question)",
        ["Punkte und Ergebnistexte kennt nur der Server – der Browser sieht nur Fragen und Sprungziele."] =
            "Only the server knows points and result texts – the browser sees just questions and jump targets.",
        ["Pfadprüfung: {0} Fragen, alle erreichbar, keine Schleifen, Ergebnisse decken 0–100 % ab."] =
            "Path check: {0} questions, all reachable, no loops, results cover 0–100 %.",
        ["Fragen"] = "Questions",
        ["Fragen-ID (stabil, für Sprünge)"] = "Question ID (stable, used for jumps)",
        ["Frage entfernen"] = "Remove question",
        ["Fragetext"] = "Question text",
        ["Thema (für den Findings-Warntext, {topic})"] = "Topic (for the findings warning text, {topic})",
        ["Options-ID"] = "Option ID",
        ["Punkte"] = "Points",
        ["Weiter mit"] = "Continue with",
        ["Nächste Frage"] = "Next question",
        ["Ende → Bewertung"] = "End → scoring",
        ["Sprung: {0} ({1})"] = "Jump: {0} ({1})",
        ["→ Ergebnis: {0}"] = "→ Result: {0}",
        ["Antwort entfernen"] = "Remove answer",
        ["Antwort hinzufügen"] = "Add answer",
        ["Frage hinzufügen"] = "Add question",
        ["Ergebnisse (nach Prozent des gegangenen Pfads)"] = "Results (by percent of the path taken)",
        ["Springt eine Weiche direkt auf ein Ergebnis, gilt dieses – unabhängig von den Punkten. Die Bereiche müssen 0–100 % lückenlos abdecken."] =
            "If a branch jumps straight to a result, that result wins – regardless of points. The ranges must cover 0–100 % without gaps.",
        ["Ergebnis-ID"] = "Result ID",
        ["Sprungziel einer Weiche"] = "Jump target of a branch",
        ["Von %"] = "From %",
        ["Bis %"] = "To %",
        ["Ergebnis entfernen"] = "Remove result",
        ["Titel"] = "Title",
        ["Text"] = "Text",
        ["Ergebnis hinzufügen"] = "Add result",
        ["Individuelle Auswertungstexte anzeigen (bis zu n Findings aus den gewählten Antworten)"] =
            "Show individual findings (up to n, taken from the chosen answers)",
        ["Höchstens"] = "At most",
        ["Findings"] = "Findings",
        ["Priorität (Fragen-IDs, kommasepariert)"] = "Priority (question IDs, comma-separated)",
        ["z. B. 2, 3, 1, 7"] = "e.g. 2, 3, 1, 7",
        ["Warntext-Vorlage (füllt bei weniger als zwei Findings über die 1-Punkt-Themen auf; {topic} wird ersetzt)"] =
            "Warning template (tops up from 1-point topics when fewer than two findings; {topic} is replaced)",
        ["Text, wenn gar nichts auffiel"] = "Text when nothing stood out",
        ["Finding (Auswertungstext, wenn diese Antwort gewählt wurde – optional)"] = "Finding (shown in the evaluation when this answer was chosen – optional)",

        // preview
        ["Bitte wählen"] = "Please choose",
        ["Absenden"] = "Submit",
        ["Weiter"] = "Next",
        ["Frage 1: {0}"] = "Question 1: {0}",
        ["Danach: weitere Fragen, ggf. Kontaktfelder, dann das Ergebnis."] = "After that: more questions, contact fields if configured, then the result.",
        ["Schematische Vorschau – Schrift, Farben und Abstände kommen auf der Website vollständig vom Theme."] =
            "Schematic preview – fonts, colors, and spacing come entirely from the website theme.",

        // submissions
        ["Alle Formulare"] = "All forms",
        ["Zurück zu {0}"] = "Back to {0}",
        ["{0} von {1}"] = "{0} of {1}",
        ["Vorherige Einsendung"] = "Previous submission",
        ["Nächste Einsendung"] = "Next submission",
        ["Zurück zu {0} · {1}"] = "Back to {0} · {1}",
        ["allen Einsendungen"] = "all submissions",
        ["Neu seit letztem Besuch"] = "New since last visit",
        ["Zu bearbeiten"] = "To handle",
        ["Wartet auf Bestätigung"] = "Waiting for confirmation",
        ["Fehler"] = "Error",
        ["CSV exportieren"] = "Export CSV",
        ["Letzter Besuch: {0}"] = "Last visit: {0}",
        ["alles als gesehen markieren"] = "mark everything as seen",
        ["Eingang"] = "Received",
        ["Formular"] = "Form",
        ["Absender"] = "Sender",
        ["Bearbeitung"] = "Handling",
        ["Bearbeiter"] = "Assignee",
        ["Meine offenen"] = "My open ones",
        ["Niemand"] = "Nobody",
        ["Bearbeiter:"] = "Assignee:",
        ["Alle Bearbeiter"] = "All assignees",
        ["Zur Auswahl stehen nur Admins, die schon einmal im Admin waren."] =
            "Only admins who have been in the admin at least once are offered.",
        ["Offene von {0}"] = "{0}'s open ones",
        ["Offene ohne Bearbeiter"] = "Open, unassigned",
        ["neu seit letztem Besuch"] = "new since last visit",
        ["Offen"] = "Open",
        ["Erledigt"] = "Done",
        ["Nichts in dieser Auswahl."] = "Nothing in this selection.",
        ["{0} Einsendungen · Seite {1} von {2}"] = "{0} submissions · page {1} of {2}",
        ["‹ Zurück"] = "‹ Back",
        ["Weiter ›"] = "Next ›",
        ["Einsendung anklicken, um Angaben und Verarbeitungsschritte zu sehen. Der blaue Punkt markiert, was seit deinem letzten Besuch dazugekommen ist."] =
            "Click a submission to see its values and processing steps. The blue dot marks what arrived since your last visit.",
        ["Einsendung"] = "Submission",

        // Einsendungs-Detail
        ["DOI bestätigt {0}"] = "DOI confirmed {0}",
        ["Bestätigungsmail erneut senden"] = "Resend confirmation email",
        ["Bestätigungsmail erneut verschickt."] = "Confirmation email sent again.",
        ["Bearbeitung:"] = "Handling:",
        ["Werte"] = "Values",
        ["Einwilligungstext"] = "Consent text",
        ["{0}/{1} Punkte auf dem Pfad ({2} %)"] = "{0}/{1} points on the path ({2} %)",
        ["direkt per Weiche"] = "straight via branch",
        ["Schritte"] = "Steps",
        ["Schritt"] = "Step",
        ["Phase"] = "Phase",
        ["Versuche"] = "Attempts",
        ["Nach Bestätigung"] = "After confirmation",
        ["Einsendung löschen"] = "Delete submission",
        ["Einsendung endgültig löschen (inkl. erzeugter PDFs)?"] = "Permanently delete this submission (including generated PDFs)?",
        ["Verlauf"] = "History",
        ["Noch keine Einträge."] = "No entries yet.",
        ["Status geändert zu {0}"] = "Status changed to {0}",
        ["Zugewiesen an {0}"] = "Assigned to {0}",
        ["Zuweisung entfernt"] = "Assignment removed",
        ["Schritt wiederholt: {0}"] = "Step retried: {0}",
        ["Fehlgeschlagene Schritte wiederholt"] = "Retried failed steps",
        ["Bestätigungsmail erneut gesendet"] = "Confirmation email resent",
        ["automatisch"] = "automatic",
        ["unbekannt"] = "unknown",
        // Status-Chips (Labels.cs)
        ["In Arbeit"] = "In progress",
        ["Fertig"] = "Finished",
        ["Ausstehend"] = "Pending",
        ["Wartet"] = "Waiting",
        ["Fehlgeschlagen"] = "Failed",
        ["Blockiert"] = "Blocked",
        ["Übersprungen"] = "Skipped",

        // statistics
        ["Formular wählen …"] = "Choose a form …",
        ["Alle Versionen"] = "All versions",
        ["Version {0}"] = "Version {0}",
        ["Zeitraum Verlauf: letzte 14 Tage"] = "Trend period: last 14 days",
        ["Wähle ein veröffentlichtes Formular."] = "Choose a published form.",
        ["{0} per Weiche beendet"] = "{0} ended via branch",
        ["mit E-Mail-Adresse ({0})"] = "with an email address ({0})",
        ["Ø normierter Punktestand"] = "Ø normalized score",
        ["DOI bestätigt ({0}) · {1} unbestätigt"] = "DOI confirmed ({0}) · {1} unconfirmed",
        ["mit fehlgeschlagenen Schritten"] = "with failed steps",
        ["Einsendungen pro Tag"] = "Submissions per day",
        ["vor 14 Tagen"] = "14 days ago",
        ["heute"] = "today",
        ["Ergebnis-Verteilung"] = "Result distribution",
        ["Antworten je Frage"] = "Answers per question",
        ["Anteil derer, die die Frage auf ihrem Pfad gesehen haben."] = "Share of those who saw the question on their path.",
        ["Herkunft (utm_source)"] = "Origin (utm_source)",
        ["Aufrufe (14 Tage)"] = "Views (14 days)",
        ["davon begonnen"] = "of which started",
        ["Abschlussquote (Einsendungen / begonnen, 14 Tage)"] = "Completion rate (submissions / started, 14 days)",

        // settings
        ["Werte kommen aus den App-Settings der Static Web App (Präfix Entriqa__) – hier nur Status und Kontrolle, geändert wird in Azure."] =
            "Values come from the Static Web App's app settings (prefix Entriqa__) – this page shows status only; changes happen in Azure.",
        ["Website"] = "Website",
        ["Basis-Adresse"] = "Base address",
        ["Nicht konfiguriert"] = "Not configured",
        ["Ohne Entriqa__Brevo__ApiKey landen Mails lokal im Dev-Mail-Sink (%TEMP%\\entriqa-devmails)."] =
            "Without Entriqa__Brevo__ApiKey, emails land in the local dev mail sink (%TEMP%\\entriqa-devmails).",
        ["Verbunden"] = "Connected",
        ["API-Schlüssel gültig, Listen abrufbar."] = "API key valid, lists retrievable.",
        ["API-Schlüssel gesetzt, aber Brevo nicht erreichbar oder Schlüssel ungültig."] = "API key set, but Brevo unreachable or key invalid.",
        ["Konfiguriert"] = "Configured",
        ["Nur nötig, wenn Quiz-PDFs erzeugt werden sollen."] = "Only needed if quiz PDFs should be generated.",
        ["Datenschutz & Aufbewahrung"] = "Privacy & retention",
        ["Einsendungen löschen nach"] = "Delete submissions after",
        ["{0} Tagen (inkl. erzeugter PDFs)"] = "{0} days (including generated PDFs)",
        ["Unbestätigte DOI löschen nach"] = "Delete unconfirmed DOI after",
        ["{0} Tagen"] = "{0} days",
        ["Deferred-Sweep nach"] = "Deferred sweep after",
        ["{0} Minuten Schonfrist"] = "{0} minutes grace period",
        ["Auto-Retry"] = "Auto retry",
        ["höchstens {0} Versuche, danach bleibt es für den Admin liegen"] = "at most {0} attempts, after that it waits for the admin",
        ["Housekeeping"] = "Housekeeping",
        ["Läuft"] = "Running",
        ["Letzter Lauf: {0} – {1}"] = "Last run: {0} – {1}",
        ["Noch kein Lauf"] = "No run yet",
        ["Der DevOps-Schedule ruft alle 15 Minuten POST /api/housekeeping auf (Vorlage: pipelines/azure-pipelines-housekeeping.yml)."] =
            "The DevOps schedule calls POST /api/housekeeping every 15 minutes (template: pipelines/azure-pipelines-housekeeping.yml).",
        ["Zugriff"] = "Access",
        ["Admin-Rollen werden über die Static Web App vergeben: Azure-Portal → Static Web App → Rollenverwaltung → Einladung mit Rolle admin. Lokal übernimmt die SWA-CLI die Auth-Emulation."] =
            "Admin roles are granted via the Static Web App: Azure portal → Static Web App → Role management → invitation with role admin. Locally, the SWA CLI emulates auth.",

        // Kontakte-Sicht
        ["Kontakte"] = "Contacts",
        ["Firmen"] = "Companies",
        ["Firma"] = "Company",
        ["Wer hat je etwas eingesendet – aggregiert aus den Einsendungen. Die Aufbewahrungsfrist begrenzt die Historie; der dauerhafte Kontakt-Zustand lebt in Brevo."] =
            "Everyone who ever submitted something – aggregated from the submissions. The retention period limits the history; the durable contact state lives in Brevo.",
        ["Suchen (E-Mail, Name, Firma)"] = "Search (email, name, company)",
        ["{0} Kontakte"] = "{0} contacts",
        ["Zuletzt"] = "Last",
        ["seit {0}"] = "since {0}",
        ["Keine Firmenangaben in den Einsendungen."] = "No company information in the submissions.",
        ["Kontakt komplett löschen"] = "Delete contact entirely",
        ["Alle {0} Einsendungen von {1} endgültig löschen (inkl. Dateien)?"] = "Permanently delete all {0} submissions from {1} (including files)?",
        ["{0} Einsendungen gelöscht."] = "{0} submissions deleted.",
        ["Öffnen"] = "Open",

        // Einwilligungen-Sicht (#2)
        ["Einwilligungen"] = "Consents",
        ["Suchen"] = "Search",
        ["Nachweis gelöscht."] = "Proof deleted.",
        ["Dieser Nachweis war bereits gelöscht."] = "This proof had already been deleted.",
        ["Nachweise zu einer E-Mail-Adresse – für die Auskunft nach Art. 15 DSGVO und für den Widerruf einzelner Einwilligungen."] =
            "Proof of consent for one email address – for an Art. 15 GDPR request and for revoking a single consent.",
        ["Zu {0} liegt keine Einwilligung vor."] = "No consent is on file for {0}.",
        ["Bestätigt"] = "Confirmed",
        ["Nicht bestätigt"] = "Not confirmed",
        ["Wortlaut der Einwilligung"] = "Consent wording",
        ["Nachweis löschen"] = "Delete proof",
        ["IP-Hash Einsendung: {0} · Bestätigung: {1}"] = "IP hash on submission: {0} · on confirmation: {1}",
        ["Einwilligungsnachweis vom {0} endgültig löschen? Der Beleg für diese Einwilligung ist danach weg."] =
            "Permanently delete the consent proof from {0}? The evidence for this consent is gone afterwards.",

        // Lizenz
        ["Lizenz"] = "License",
        ["Lizenziert"] = "Licensed",
        ["Plan {0} · gültig bis {1}"] = "Plan {0} · valid until {1}",
        ["Unlizenziert – nur für Entwicklung"] = "Unlicensed – development only",
        ["Unlizenziert – nur für Entwicklung."] = "Unlicensed – development only.",
        ["Produktivbetrieb erfordert eine Entriqa-Lizenz (App-Setting Entriqa__LicenseKey). Bezug: andrekraemer.de/entriqa."] =
            "Production use requires an Entriqa license (app setting Entriqa__LicenseKey). Get one at andrekraemer.de/entriqa.",
        ["Details"] = "Details",

        // error messages
        ["Nicht angemeldet."] = "Not signed in.",
        ["Bitte zuerst anmelden: /.auth/login/aad öffnen, Benutzername wählen und im Rollen-Feld 'admin' eintragen (lokal simuliert die SWA-CLI den Login)."] =
            "Please sign in first: open /.auth/login/aad, choose a username, and enter 'admin' in the roles field (locally the SWA CLI simulates the login).",
    };
}
