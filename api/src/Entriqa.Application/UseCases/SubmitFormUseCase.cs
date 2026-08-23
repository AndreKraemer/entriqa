using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Entriqa.Domain.Validation;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Der Kernablauf: Definition laden → Spam-Prüfung → Validierung → Quiz auswerten → speichern → Inline-Schritte → Antwort.
/// Linear und sichtbar, keine Basisklasse (Solution Standard §13.2).
/// </summary>
internal sealed class SubmitFormUseCase(
    ITryGetPublishedFormQuery getPublished,
    IStoreSubmissionCommand store,
    ISaveSubmissionCommand save,
    ITryConsumeNonceCommand consumeNonce,
    IRegisterRateLimitHitCommand rateLimit,
    IStoreArtifactPort artifacts,
    FormTokenService tokens,
    IpHasher ipHasher,
    SubmissionPipelineService pipeline,
    IOptions<EntriqaOptions> options,
    TimeProvider time,
    ILogger<SubmitFormUseCase> log) : ISubmitFormUseCase
{
    // Table-Storage-Property-Grenze ist 64 KB (32K UTF-16-Zeichen); Puffer für JSON-Overhead und Escaping.
    private const int MaxValuesChars = 28_000;

    public async Task<SubmitFormResult> ExecuteAsync(SubmitFormRequest request, CancellationToken ct = default)
    {
        var o = options.Value;
        var published = await getPublished.ExecuteAsync(request.Slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{request.Slug}' ist nicht veröffentlicht.");
        var locale = published.Definition.MatchLocale(request.Lang);
        var def = published.Definition.Localize(locale);                    // ab hier ist alles einsprachig (Validator, Steps, Quiz)

        // 1. Spam: Honeypot → stillschweigend "Erfolg", damit der Bot nichts lernt.
        if (!string.IsNullOrEmpty(request.Honeypot))
        {
            log.LogInformation("Honeypot ausgelöst für {Slug}", request.Slug);
            return new SubmitFormResult("ignored", CompletionOf(def), null, null);
        }

        // 2. Token: signiert, zum Formular passend, mindestens MinSubmitSeconds alt, höchstens MaxSubmitHours.
        //    Die Nonce wird erst NACH der Validierung verbraucht (Schritt 5) – ein Validierungsfehler
        //    darf den Token nicht verbrennen, sonst scheitert der korrigierte zweite Versuch mit "replayed".
        var payload = tokens.Validate(request.Token, FormTokenService.KindForm, request.Slug,
            TimeSpan.FromSeconds(o.MinSubmitSeconds), TimeSpan.FromHours(o.MaxSubmitHours));

        // 3. Rate-Limit je IP-Hash und Zeitfenster.
        var ipHash = ipHasher.Hash(request.ClientIp);
        if (ipHash is not null)
        {
            var now = time.GetUtcNow();
            var window = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / o.RateLimitWindowMinutes * o.RateLimitWindowMinutes, 0, TimeSpan.Zero);
            if (await rateLimit.ExecuteAsync(ipHash, window, ct) > o.RateLimitPerWindow)
                throw new AppException(ErrorCodes.RateLimited, "Zu viele Einsendungen – bitte später erneut versuchen.", 429);
        }

        // 4. Validierung gegen die Definition (alle Fehler auf einmal) und Quiz-Auswertung (läuft den Pfad selbst nach).
        var values = CollectValues(def, request.Values);
        if (values.Sum(kv => kv.Key.Length + kv.Value.Length + 8) > MaxValuesChars)
            throw new ValidationException(new[] { new FieldError("", ValidationMessages.Get(locale, ValidationMessages.TooBig)) });
        FormSubmissionValidator.ValidateAndThrow(def, values, request.Answers, locale, o.ExtraFreemailDomains);
        var quiz = def.Quiz is null ? null : QuizEngine.Evaluate(def.Quiz, request.Answers!);

        // 5. Nonce verbrauchen (jetzt erst – die Eingaben sind gültig) und speichern, bevor irgendein Schritt läuft.
        if (!await consumeNonce.ExecuteAsync(payload.Nonce, payload.IssuedAt.AddHours(o.MaxSubmitHours), ct))
            throw new SecurityTokenException(ErrorCodes.TokenReplayed, "Dieses Formular wurde bereits abgeschickt – bitte Seite neu laden.");
        var nowUtc = time.GetUtcNow();
        var rowKey = $"{DateTimeOffset.MaxValue.Ticks - nowUtc.Ticks:D19}-{Guid.NewGuid():N}"[..28];
        var submission = new Submission
        {
            Id = $"{def.Slug}:{rowKey}",
            Slug = def.Slug,
            Version = published.Version,
            CreatedAt = nowUtc,
            Locale = locale,
            Values = values,
            Email = def.EmailField is { } ef && values.TryGetValue(ef.Id, out var email) && email.Length > 0 ? email.Trim() : null,
            FirstName = FirstNameOf(def, values),
            Source = def.Fields.FirstOrDefault(f => f.Type == FieldTypes.Hidden && f.Source == "utm_source") is { } sf ? values.GetValueOrDefault(sf.Id) : null,
            IpHash = ipHash,
            Quiz = quiz,
            ConsentText = def.ConsentField?.Text?.ToString(),
            StepRuns = pipeline.CreateRuns(def),
            Handling = def.Handling ? HandlingStates.Open : HandlingStates.None,
        };
        await AdoptUploadsAsync(def, submission, ct);
        await store.ExecuteAsync(submission, ct);

        // 6. Inline-Schritte; Deferred bleiben Pending und werden per Run-Token vom Client angestoßen.
        var deferredLeft = await pipeline.RunAsync(submission, def, published.Version, RunMode.Inline, null, ct);
        await save.ExecuteAsync(submission, ct);

        QuizResultView? quizView = null;
        if (quiz is not null)
        {
            var r = def.Quiz!.Results.First(x => x.Id == quiz.ResultId);
            quizView = new QuizResultView(r.Id, r.Title.ToString(), r.Body.ToString(), quiz.Points, quiz.MaxPoints, quiz.Pct, quiz.Findings);
        }
        return new SubmitFormResult(submission.Id, CompletionOf(def), quizView,
            deferredLeft ? tokens.Issue(FormTokenService.KindRun, submission.Id) : null);
    }

    /// <summary>
    /// Hochgeladene Dateien wandern von uploads/ (verwaisbar, Housekeeping räumt nach 2 Tagen)
    /// nach attachments/ und in die Artefakte der Einsendung – damit hängen sie an deren Lebenszyklus
    /// (Löschen/Retention entsorgt die Blobs mit).
    /// </summary>
    private async Task AdoptUploadsAsync(FormDefinition def, Submission submission, CancellationToken ct)
    {
        foreach (var f in def.Fields.Where(x => x.Type == FieldTypes.File))
        {
            if (!submission.Values.TryGetValue(f.Id, out var raw)) continue;
            var handle = UploadHandle.TryParse(raw);
            if (handle is null || !handle.Path.StartsWith("uploads/", StringComparison.Ordinal)) continue;
            byte[] content;
            try { content = await artifacts.ReadAsync(handle.Path, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ValidationException(new[] { new FieldError(f.Id,
                    ValidationMessages.Get(submission.Locale, ValidationMessages.UploadInvalid)) });
            }
            var target = $"attachments/{Guid.NewGuid():N}/{handle.Name}";
            await artifacts.StoreAsync(target, content, "application/octet-stream", ct);
            await artifacts.DeleteAsync(handle.Path, ct);
            submission.Values[f.Id] = (handle with { Path = target }).ToJson();
            submission.Artifacts[$"upload:{f.Id}"] = target;
        }
    }

    private static CompletionView CompletionOf(FormDefinition def) =>
        new(def.Completion.Mode, def.Completion.Message?.ToString(), def.Completion.Url?.ToString());

    /// <summary>Nur Werte zu definierten Feldern übernehmen; Mehrfachauswahl normalisieren; hidden-Feste-Werte setzen.</summary>
    private static Dictionary<string, string> CollectValues(FormDefinition def, Dictionary<string, string> incoming)
    {
        var values = new Dictionary<string, string>();
        foreach (var f in def.Fields)
        {
            if (FieldTypes.IsLayout(f.Type)) continue;
            if (f.Type == FieldTypes.Hidden && f.Source is { } s && s.StartsWith("fixed:", StringComparison.Ordinal)) { values[f.Id] = s[6..]; continue; }
            if (!incoming.TryGetValue(f.Id, out var raw) || raw is null) continue;
            var v = raw.Trim();
            if (f.Type == FieldTypes.Multiselect) v = string.Join(", ", FormSubmissionValidator.SplitMulti(v));
            if (v.Length > 0) values[f.Id] = v.Length > 10_000 ? v[..10_000] : v;
        }
        return values;
    }

    private static string? FirstNameOf(FormDefinition def, Dictionary<string, string> values)
    {
        static string L(FieldDefinition f) => f.Label.ToString();
        var f = def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text
                    && (L(x).Contains("Vorname", StringComparison.OrdinalIgnoreCase) || L(x).Contains("first name", StringComparison.OrdinalIgnoreCase)))
             ?? def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text && L(x).Equals("Name", StringComparison.OrdinalIgnoreCase));
        if (f is null || !values.TryGetValue(f.Id, out var v)) return null;
        return L(f).Equals("Name", StringComparison.OrdinalIgnoreCase) ? v.Split(' ', 2)[0] : v;
    }
}
