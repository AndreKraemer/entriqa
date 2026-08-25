namespace Entriqa.Admin.Services;

/// <summary>Starter templates for new forms, one per type with a sensible pipeline.</summary>
public static class FormTemplates
{
    public static string For(string type, string slug) => type switch
    {
        "leadmagnet" => NewLeadMagnet(slug),
        "quiz" => NewQuiz(slug),
        _ => NewContact(slug),
    };

    /// <summary>Derive the slug from the name (umlauts, special characters).</summary>
    public static string Slugify(string name)
    {
        var s = name.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var chars = s.Select(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    public static string NewLeadMagnet(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "name": "Neuer Lead-Magnet",
      "type": "leadmagnet",
      "locales": ["de"],
      "intro": "Kurz beschreiben, was es zum Download gibt.",
      "submitLabel": "Jetzt anfordern",
      "handling": false,
      "fields": [
        { "id": "email", "type": "email", "label": "E-Mail", "required": true, "businessOnly": false },
        { "id": "vorname", "type": "text", "label": "Vorname" },
        { "id": "consent", "type": "consent", "label": "Einwilligung", "required": true, "text": "Ich möchte den Download erhalten und stimme zu, per E-Mail weitere Informationen zu bekommen. Abmeldung jederzeit." },
        { "id": "src", "type": "hidden", "label": "Quelle", "source": "utm_source" }
      ],
      "pipeline": [
        { "id": "s1", "step": "doi.request", "when": "always", "config": { "templateId": 1 } },
        { "id": "s2", "step": "brevo.contact", "when": "always", "config": { "listIds": [1] } },
        { "id": "s3", "step": "leadmagnet.link", "when": "always", "config": { "blob": "", "hours": 48 } },
        { "id": "s4", "step": "brevo.mail", "when": "always", "config": { "templateId": 2, "attach": "download" } }
      ],
      "completion": { "mode": "message", "message": "Fast geschafft – bitte bestätige deine E-Mail-Adresse. Danach kommt der Download." }
    }
    """;

    public static string NewQuiz(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "name": "Neues Quiz",
      "type": "quiz",
      "locales": ["de"],
      "intro": "Ein paar Fragen, ein ehrliches Ergebnis.",
      "handling": false,
      "fields": [
        { "id": "email", "type": "email", "label": "E-Mail (optional, für das Ergebnis)" },
        { "id": "consent", "type": "consent", "label": "Einwilligung", "text": "Ich stimme zu, mein Ergebnis per E-Mail zu erhalten." }
      ],
      "quiz": {
        "scoring": "sum",
        "collectEmail": "optional",
        "questions": [
          { "id": "q1", "text": "Erste Frage", "options": [
            { "id": "a", "label": "Antwort A", "points": 0 },
            { "id": "b", "label": "Antwort B", "points": 2 } ] }
        ],
        "results": [
          { "id": "unten", "minPct": 0, "maxPct": 49, "title": "Ausbaufähig", "body": "…" },
          { "id": "oben", "minPct": 50, "maxPct": 100, "title": "Stark", "body": "…" }
        ]
      },
      "pipeline": [],
      "completion": { "mode": "result" }
    }
    """;

    public static string NewContact(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "name": "Neues Formular",
      "type": "contact",
      "locales": ["de"],
      "intro": "Kurzer Einleitungstext.",
      "submitLabel": "Absenden",
      "handling": true,
      "fields": [
        { "id": "name", "type": "text", "label": "Name", "required": true },
        { "id": "email", "type": "email", "label": "E-Mail", "required": true, "businessOnly": false },
        { "id": "message", "type": "textarea", "label": "Nachricht", "required": true },
        { "id": "consent", "type": "consent", "label": "Einwilligung", "required": true, "text": "Ich stimme der Verarbeitung meiner Daten zu." }
      ],
      "pipeline": [
        { "id": "s1", "step": "notify.mail", "when": "always", "config": { "to": "info@example.org", "templateId": 1 } }
      ],
      "completion": { "mode": "message", "message": "Danke! Deine Nachricht ist angekommen." }
    }
    """;
}
