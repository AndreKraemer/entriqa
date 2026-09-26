using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Entriqa.Domain.Validation;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Test mode (#21): loads the <em>draft</em> (never the published version - AC2), validates and scores it
/// exactly as a real submission would, then walks the whole pipeline without side effects. No anti-spam
/// (honeypot, token, rate limit) - the caller is an authenticated admin - and no persistence: this use case
/// never depends on the store/save ports, so no test submission can reach the inbox, reporting or history (AC7).
/// </summary>
internal sealed class RunFormTestUseCase(
    ITryGetFormDraftQuery getDraft,
    SubmissionPipelineService pipeline,
    AppointmentOfferService offer,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : IRunFormTestUseCase
{
    public async Task<FormTestResult> ExecuteAsync(FormTestRequest request, CancellationToken ct = default)
    {
        var o = options.Value;
        var draft = await getDraft.ExecuteAsync(request.Slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormUnknown, AppException.Args("slug", request.Slug));
        var locale = draft.Definition.MatchLocale(request.Lang);
        var def = draft.Definition.Localize(locale);

        // Same validation and scoring as SubmitFormUseCase - a test must catch what a real submission would (AC3).
        var values = CollectValues(def, request.Values);
        var offered = await offer.ForAsync(def, ct);
        FormSubmissionValidator.ValidateAndThrow(def, values, request.Answers, locale, o.ExtraFreemailDomains, offered);
        var quiz = def.Quiz is null ? null : QuizEngine.Evaluate(def.Quiz, request.Answers!);

        var submission = new Submission
        {
            Id = $"{def.Slug}:test",
            Slug = def.Slug,
            Version = draft.PublishedVersion,
            CreatedAt = time.GetUtcNow(),
            Locale = locale,
            Values = values,
            Email = def.EmailField is { } ef && values.TryGetValue(ef.Id, out var email) && email.Length > 0 ? email.Trim() : null,
            FirstName = FirstNameOf(def, values),
            Quiz = quiz,
            ConsentText = def.ConsentField?.Text?.ToString(),
            StepRuns = pipeline.CreateRuns(def),
        };

        var steps = await pipeline.RunTestAsync(submission, def, draft.PublishedVersion, request.AdminEmail, ct);
        return new FormTestResult(steps, request.AdminEmail);
    }

    // Mirrors SubmitFormUseCase: take over defined fields only, normalize multi-select, set fixed hidden values.
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
