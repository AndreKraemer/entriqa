using System.Text;

namespace Entriqa.Admin.Services;

/// <summary>
/// Mirrors the markup that forms.js produces for this form - as a reference for the web developer
/// who styles the theme. Plus the class reference (a stable contract, spec section 8/v1).
/// </summary>
public static class MarkupGenerator
{
    public static string Render(FormModel m)
    {
        var loc = m.Locales[0];
        var sb = new StringBuilder();
        void L(int depth, string text) => sb.Append(new string(' ', depth * 2)).AppendLine(text);
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);

        L(0, $"<form class=\"eq-form eq-form--{m.Type} eq-form--{m.Slug}\" data-form=\"{m.Slug}\" novalidate>");
        if (m.Intro.Get(loc) is { Length: > 0 } intro) L(1, $"<p class=\"eq-form__intro\">{E(intro)}</p>");

        foreach (var f in m.Fields)
        {
            var id = $"eq-{m.Slug}-{f.Id}";
            var required = f.Required || f.Type == "appointment";   // #7: forms.js always marks the appointment field required
            var req = required ? " eq-field--required" : "";
            switch (f.Type)
            {
                case "section": L(1, $"<h3 class=\"eq-section\">{E(f.Label.Get(loc))}</h3>"); continue;
                case "divider": L(1, "<hr class=\"eq-divider\">"); continue;
                case "page":
                    L(1, "<!-- Seitenwechsel: forms.js legt je Seite ein <div class=\"eq-page\" data-page=\"n\"> um die folgenden Felder,");
                    L(1, "     darüber <p class=\"eq-form__pages\"> als Seitenanzeige, darunter <div class=\"eq-actions eq-page__nav\"> mit Zurück/Weiter -->");
                    continue;
                case "hidden": L(1, $"<input type=\"hidden\" name=\"{f.Id}\">  <!-- Quelle: {f.Source} -->"); continue;
            }
            L(1, $"<div class=\"eq-field eq-field--{f.Type}{req}\" data-field=\"{f.Id}\">");
            if (f.Type is "consent" or "checkbox")
            {
                L(2, $"<label class=\"eq-field__label\" for=\"{id}\">");
                L(3, $"<input class=\"eq-field__control\" type=\"checkbox\" id=\"{id}\" name=\"{f.Id}\"{(required ? " required" : "")}>");
                L(3, $"<span>{E((f.Type == "consent" ? f.ConsentText : f.Label).Get(loc))}{(required ? " <span class=\"eq-field__required\">*</span>" : "")}</span>");
                L(2, "</label>");
            }
            else
            {
                L(2, $"<label class=\"eq-field__label\" for=\"{id}\">{E(f.Label.Get(loc))}{(required ? " <span class=\"eq-field__required\">*</span>" : "")}</label>");
                switch (f.Type)
                {
                    case "textarea": L(2, $"<textarea class=\"eq-field__control\" id=\"{id}\" name=\"{f.Id}\"{(required ? " required" : "")}></textarea>"); break;
                    case "select":
                        L(2, $"<select class=\"eq-field__control\" id=\"{id}\" name=\"{f.Id}\"{(required ? " required" : "")}>");
                        L(3, "<option value=\"\">Bitte wählen</option>");
                        foreach (var o in f.Options) L(3, $"<option>{E(o.Get(loc))}</option>");
                        L(2, "</select>");
                        break;
                    case "appointment":
                        L(2, $"<select class=\"eq-field__control\" id=\"{id}\" name=\"{f.Id}\" required>");
                        L(3, "<option value=\"\">Bitte wählen</option>");
                        L(3, "<option value=\"{Termin-Id}\">Di., 13.10.2026, 10:00–12:00 · Titel</option>  <!-- je buchbarem Termin, in der Zeitzone des Besuchers -->");
                        L(2, "</select>");
                        L(2, "<!-- Ohne buchbaren Termin statt der Auswahl: <p class=\"eq-field__notice\" role=\"status\">…</p>, der Absende-Button ist gesperrt -->");
                        break;
                    case "multiselect":
                        L(2, "<div class=\"eq-field__options\" role=\"group\">");
                        foreach (var o in f.Options) L(3, $"<label class=\"eq-field__option\"><input class=\"eq-field__control\" type=\"checkbox\" name=\"{f.Id}\" value=\"{E(o.Get(loc))}\"> {E(o.Get(loc))}</label>");
                        L(2, "</div>");
                        break;
                    case "rating":
                        L(2, "<div class=\"eq-field__scale\" role=\"radiogroup\">");
                        L(3, $"<label class=\"eq-field__scale-option\"><input type=\"radio\" name=\"{f.Id}\" value=\"1\"> <span>1</span></label>  <!-- … bis n -->");
                        L(2, "</div>");
                        break;
                    case "file":
                        L(2, $"<input class=\"eq-field__control\" type=\"file\" id=\"{id}\" name=\"{f.Id}\"{(required ? " required" : "")} accept=\"…\">");
                        L(2, "<p class=\"eq-field__file-status\" hidden></p>  <!-- Upload-Status -->");
                        break;
                    default:
                        var type = f.Type is "email" or "number" or "date" or "tel" ? f.Type : "text";
                        L(2, $"<input class=\"eq-field__control\" type=\"{type}\" id=\"{id}\" name=\"{f.Id}\"{(required ? " required" : "")}>");
                        break;
                }
                if (f.Help.Get(loc) is { Length: > 0 } help) L(2, $"<p class=\"eq-field__help\">{E(help)}</p>");
            }
            L(2, "<p class=\"eq-field__error\" hidden></p>");
            L(1, "</div>");
        }

        if (m.Quiz is { Questions.Count: > 0 } quiz)
        {
            var q = quiz.Questions[0];
            L(1, "<!-- Quiz: eine Frage je Schritt; Kontaktfelder erscheinen erst vor dem Ergebnis -->");
            L(1, "<div class=\"eq-quiz\">");
            L(2, "<div class=\"eq-quiz__progress\" role=\"progressbar\"><div class=\"eq-quiz__progress-bar\" style=\"width:17%\"></div></div>");
            L(2, $"<fieldset class=\"eq-quiz__question\" data-question=\"{q.Id}\">");
            L(3, $"<legend class=\"eq-quiz__question-text\">{E(q.Text.Get(loc))}</legend>");
            foreach (var o in q.Options) L(3, $"<label class=\"eq-quiz__option\"><input type=\"radio\" name=\"{q.Id}\" value=\"{o.Id}\"> <span>{E(o.Label.Get(loc))}</span></label>");
            L(2, "</fieldset>");
            L(2, "<div class=\"eq-quiz__nav\"><button type=\"button\" class=\"eq-submit\" data-action=\"next\">Weiter</button></div>");
            L(2, "<div class=\"eq-quiz__result\" hidden><h3 class=\"eq-quiz__result-title\">…</h3><div class=\"eq-quiz__result-body\">…</div><ul class=\"eq-quiz__findings\"></ul></div>");
            L(1, "</div>");
        }

        L(1, "<input type=\"text\" name=\"website\" tabindex=\"-1\" autocomplete=\"off\" hidden aria-hidden=\"true\">  <!-- Honeypot -->");
        if (m.Quiz is null)
            L(1, $"<div class=\"eq-actions\"><button type=\"submit\" class=\"eq-submit\">{E(m.SubmitLabel.Get(loc) is { Length: > 0 } s ? s : "Absenden")}</button></div>");
        L(1, "<div class=\"eq-message\" hidden role=\"status\" aria-live=\"polite\"></div>");
        L(0, "</form>");
        return sb.ToString();
    }

    public static readonly (string Class, string Meaning)[] Classes =
    {
        ("eq-form", "Wurzelelement (<form>); Modifier --contact | --leadmagnet | --quiz sowie --{slug}; Zustände --busy, --done"),
        ("eq-form__intro", "Einleitungstext"),
        ("eq-field", "Feldcontainer; Modifier je Typ (--text, --email, …), --required, --error; data-field=\"{id}\""),
        ("eq-field__label", "Beschriftung (<label>)"),
        ("eq-field__required", "Pflicht-Stern im Label"),
        ("eq-field__control", "Eingabeelement (input/select/textarea)"),
        ("eq-field__help", "Hilfetext unter dem Feld"),
        ("eq-field__error", "Fehlermeldung unter dem Feld"),
        ("eq-field__options", "Container der Checkboxen bei Mehrfachauswahl"),
        ("eq-field__option", "Eine Checkbox-Zeile der Mehrfachauswahl"),
        ("eq-field__scale", "Container der Bewertungsskala (role=radiogroup)"),
        ("eq-field__scale-option", "Eine Stufe der Bewertungsskala (label mit Radio)"),
        ("eq-field__file-status", "Status unter einem Datei-Feld; sichtbar während und nach dem Upload"),
        ("eq-field__notice", "Hinweis statt der Terminauswahl, wenn kein Termin mehr buchbar ist; der Absende-Button ist dann gesperrt"),
        ("eq-section", "Zwischenüberschrift"),
        ("eq-divider", "Trennlinie (<hr>)"),
        ("eq-page", "Eine Seite eines mehrseitigen Formulars (data-page=\"n\")"),
        ("eq-page__nav", "Zurück/Weiter-Leiste einer Seite; trägt zusätzlich eq-actions"),
        ("eq-form__pages", "Seitenanzeige über einem mehrseitigen Formular (aria-live)"),
        ("eq-actions", "Container für den Absende-Button"),
        ("eq-submit", "Absende-Button (auch der Weiter-Button im Quiz)"),
        ("eq-message", "Rückmeldung; Modifier --success | --error"),
        ("eq-quiz", "Quiz-Container"),
        ("eq-quiz__progress", "Fortschrittsbalken des Quiz"),
        ("eq-quiz__progress-bar", "Füllung des Fortschrittsbalkens (Breite per style – einzige Inline-Ausnahme)"),
        ("eq-quiz__question", "Aktuelle Frage (fieldset, data-question)"),
        ("eq-quiz__question-text", "Fragetext (legend)"),
        ("eq-quiz__option", "Eine Antwort (label mit Radio); Modifier --selected"),
        ("eq-quiz__nav", "Zurück/Weiter-Leiste des Quiz"),
        ("eq-quiz__back", "Zurück-Button in der Quiz-Navigation"),
        ("eq-quiz__contact", "Kontaktfelder-Schritt vor dem Ergebnis"),
        ("eq-quiz__result", "Ergebnis-Box (erscheint am Ende des Quiz)"),
        ("eq-quiz__result-title", "Überschrift des Ergebnisses"),
        ("eq-quiz__result-body", "Fließtext des Ergebnisses"),
        ("eq-quiz__findings", "Liste der individuellen Befunde (<ul>)"),
        ("eq-quiz__finding", "Ein einzelner Befund (<li>)"),
        ("eq-loading", "Platzhalter beim Laden"),
    };
}
