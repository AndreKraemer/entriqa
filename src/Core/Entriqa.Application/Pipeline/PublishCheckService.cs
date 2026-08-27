using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;

namespace Entriqa.Application.Pipeline;

/// <summary>The check rules applied when publishing - fields, quiz path, pipeline consistency. Blocks on problems.</summary>
public sealed class PublishCheckService(SubmissionPipelineService pipeline)
{
    public IReadOnlyList<string> Check(FormDefinition form)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(form.Slug) || form.Slug.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
            issues.Add("Slug darf nur Kleinbuchstaben, Ziffern und Bindestriche enthalten.");
        for (var fi = 0; fi < form.Fields.Count; fi++)
        {
            var f = form.Fields[fi];
            if (!FieldTypes.All.Contains(f.Type)) issues.Add($"Feld '{f.Id}': unbekannter Typ '{f.Type}'.");
            if (f.Type is FieldTypes.Select or FieldTypes.Multiselect && (f.Options is null || f.Options.Count == 0)) issues.Add($"Feld '{f.Label}': keine Optionen.");
            if (f.Type == FieldTypes.Consent && (f.Text is null || f.Text.IsEmpty)) issues.Add($"Feld '{f.Label}': Einwilligungstext fehlt.");
            if (f.BusinessOnly && f.Type != FieldTypes.Email) issues.Add($"Feld '{f.Label}': businessOnly gilt nur für E-Mail-Felder.");
            if (f.Type == FieldTypes.Rating && f.Max is < 2 or > 10) issues.Add($"Feld '{f.Label}': Bewertungsskala braucht 2–10 Stufen.");
            if (f.VisibleIf is { } cond)
            {
                var refIndex = form.Fields.ToList().FindIndex(x => x.Id == cond.Field);
                var refField = refIndex >= 0 ? form.Fields[refIndex] : null;
                if (refField is null) issues.Add($"Feld '{f.Id}': Sichtbarkeitsbedingung zeigt auf unbekanntes Feld '{cond.Field}'.");
                else if (refIndex >= fi) issues.Add($"Feld '{f.Id}': Sichtbarkeitsbedingung muss auf ein früheres Feld zeigen.");
                else if (FieldTypes.IsLayout(refField.Type) || refField.Type == FieldTypes.Hidden)
                    issues.Add($"Feld '{f.Id}': Sichtbarkeitsbedingung kann nicht auf Layout- oder versteckte Felder zeigen.");
                else if (cond.Options is { Count: > 0 } idx)
                {
                    if (refField.Options is not { Count: > 0 } opts) issues.Add($"Feld '{f.Id}': Bedingungs-Optionen, aber '{cond.Field}' hat keine Optionen.");
                    else if (idx.Any(i => i < 0 || i >= opts.Count)) issues.Add($"Feld '{f.Id}': Bedingungs-Option außerhalb des Bereichs von '{cond.Field}'.");
                }
            }
        }
        if (form.Fields.GroupBy(f => f.Id).Any(g => g.Count() > 1)) issues.Add("Feld-IDs sind nicht eindeutig.");
        if (form.Quiz is not null && form.Fields.Any(f => f.Type == FieldTypes.Page))
            issues.Add("Seitenumbrüche wirken nur in normalen Formularen – ein Quiz führt bereits Frage für Frage.");

        foreach (var lang in form.EffectiveLocales)
            foreach (var missing in MissingTexts(form, lang))
                issues.Add($"Sprache '{lang}': Text fehlt für {missing}.");

        if (form.Quiz is not null) issues.AddRange(QuizEngine.Check(form.Quiz).Select(x => "Quiz: " + x));
        if (form.Quiz?.CollectEmail == "none" && form.Fields.Any(f => f.Required && !FieldTypes.IsLayout(f.Type) && f.Type != FieldTypes.Hidden))
            issues.Add("Quiz ohne Kontaktschritt (collectEmail: none), aber Pflichtfelder definiert – Teilnehmer könnten sie nie ausfüllen.");

        var produced = new HashSet<string>();
        var doiCount = 0;
        var listWithoutDoi = false;
        var seenDoi = false;
        for (var i = 0; i < form.Pipeline.Count; i++)
        {
            var st = form.Pipeline[i];
            var step = pipeline.TryResolve(st.Step);
            if (step is null) { issues.Add($"Schritt {i + 1}: unbekannter Schritt '{st.Step}'."); continue; }
            var n = $"Schritt {i + 1} ({step.Name})";
            foreach (var need in step.Needs)
            {
                if (need == StepNeed.EmailField && form.EmailField is null) issues.Add($"{n}: Formular hat kein E-Mail-Feld.");
                if (need == StepNeed.ConsentField && form.ConsentField is null) issues.Add($"{n}: Formular hat keine Einwilligung.");
            }
            if (step.SplitsPhase && ++doiCount > 1) issues.Add($"{n}: Double-Opt-In ist schon vorhanden.");
            if (st.Step == "brevo.contact" && !seenDoi) listWithoutDoi = true;
            issues.AddRange(step.CheckConfig(st.Config, form, produced).Select(x => $"{n}: {x}"));
            if (step.Produces is not null) produced.Add(step.Produces);
            if (step.SplitsPhase) seenDoi = true;
        }
        if (listWithoutDoi)
            issues.Add("\"Kontakt in Brevo anlegen\" ohne vorheriges Double-Opt-In: Adressen landen unbestätigt in einer Liste. Bewusste Ausnahme nur, wenn das Formular keine Marketing-Einwilligung einholt.");

        return issues.Distinct().ToList();
    }

    /// <summary>Every multi-language text that has no version for the declared language (a plain string covers all).</summary>
    private static IEnumerable<string> MissingTexts(FormDefinition form, string lang)
    {
        IEnumerable<(string Name, LText? T)> All()
        {
            yield return ("intro", form.Intro);
            yield return ("submitLabel", form.SubmitLabel);
            yield return ("completion.message", form.Completion.Message);
            yield return ("completion.url", form.Completion.Url);
            foreach (var f in form.Fields)
            {
                yield return ($"Feld '{f.Id}' (label)", f.Label);
                yield return ($"Feld '{f.Id}' (placeholder)", f.Placeholder);
                yield return ($"Feld '{f.Id}' (help)", f.Help);
                yield return ($"Feld '{f.Id}' (text)", f.Text);
                foreach (var (o, i) in (f.Options ?? Array.Empty<LText>() as IReadOnlyList<LText>).Select((o, i) => (o, i)))
                    yield return ($"Feld '{f.Id}' (Option {i + 1})", o);
            }
            if (form.Quiz is { } q)
            {
                foreach (var qq in q.Questions)
                {
                    yield return ($"Frage '{qq.Id}'", qq.Text);
                    yield return ($"Frage '{qq.Id}' (topic)", qq.Topic);
                    foreach (var o in qq.Options)
                    {
                        yield return ($"Frage '{qq.Id}', Option '{o.Id}'", o.Label);
                        yield return ($"Frage '{qq.Id}', Option '{o.Id}' (finding)", o.Finding);
                    }
                }
                foreach (var r in q.Results)
                {
                    yield return ($"Ergebnis '{r.Id}' (title)", r.Title);
                    yield return ($"Ergebnis '{r.Id}' (body)", r.Body);
                }
                yield return ("findings.warningTemplate", q.Findings?.WarningTemplate);
                yield return ("findings.emptyText", q.Findings?.EmptyText);
            }
        }

        foreach (var (name, t) in All())
            if (t is not null && !t.IsEmpty && !t.Covers(lang)) yield return name;
    }
}
