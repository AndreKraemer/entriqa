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
/// The core flow: load the definition -> spam check -> validation -> quiz scoring -> save -> inline steps -> response.
/// Linear and visible, no base class (Solution Standard §13.2).
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
    // The table storage property limit is 64 KB (32K UTF-16 characters); buffer for JSON overhead and escaping.
    private const int MaxValuesChars = 28_000;

    public async Task<SubmitFormResult> ExecuteAsync(SubmitFormRequest request, CancellationToken ct = default)
    {
        var o = options.Value;
        var published = await getPublished.ExecuteAsync(request.Slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{request.Slug}' ist nicht veröffentlicht.");
        var locale = published.Definition.MatchLocale(request.Lang);
        var def = published.Definition.Localize(locale);                    // from here on everything is single-language (validator, steps, quiz)

        // 1. Spam: honeypot -> silent "success" so that the bot learns nothing.
        if (!string.IsNullOrEmpty(request.Honeypot))
        {
            log.LogInformation("Honeypot ausgelöst für {Slug}", request.Slug);
            return new SubmitFormResult("ignored", CompletionOf(def), null, null);
        }

        // 2. Token: signed, matching the form, at least MinSubmitSeconds old, at most MaxSubmitHours.
        //    The nonce is only consumed AFTER the validation (step 5) - a validation error
        //    must not burn the token, or the corrected second attempt fails with "replayed".
        var payload = tokens.Validate(request.Token, FormTokenService.KindForm, request.Slug,
            TimeSpan.FromSeconds(o.MinSubmitSeconds), TimeSpan.FromHours(o.MaxSubmitHours));

        // 3. Rate limit per IP hash and time window.
        var ipHash = ipHasher.Hash(request.ClientIp);
        if (ipHash is not null)
        {
            var now = time.GetUtcNow();
            var window = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / o.RateLimitWindowMinutes * o.RateLimitWindowMinutes, 0, TimeSpan.Zero);
            if (await rateLimit.ExecuteAsync(ipHash, window, ct) > o.RateLimitPerWindow)
                throw new AppException(ErrorCodes.RateLimited, "Zu viele Einsendungen – bitte später erneut versuchen.", 429);
        }

        // 4. Validation against the definition (all errors at once) and quiz scoring (walks the path itself).
        var values = CollectValues(def, request.Values);
        if (values.Sum(kv => kv.Key.Length + kv.Value.Length + 8) > MaxValuesChars)
            throw new ValidationException(new[] { new FieldError("", ValidationMessages.Get(locale, ValidationMessages.TooBig)) });
        FormSubmissionValidator.ValidateAndThrow(def, values, request.Answers, locale, o.ExtraFreemailDomains);
        var quiz = def.Quiz is null ? null : QuizEngine.Evaluate(def.Quiz, request.Answers!);

        // 5. Consume the nonce (only now - the input is valid) and save before any step runs.
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

        // 6. Inline steps; deferred ones stay pending and are triggered by the client with the run token.
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
    /// Uploaded files move from uploads/ (orphanable, housekeeping clears them after 2 days)
    /// to attachments/ and into the artifacts of the submission - that way they hang on its life cycle
    /// (deletion and retention dispose of the blobs with it).
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

    /// <summary>Take over values of defined fields only; normalize multi-select; set fixed hidden values.</summary>
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
