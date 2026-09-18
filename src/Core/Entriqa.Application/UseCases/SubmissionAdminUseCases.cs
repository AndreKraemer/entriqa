using Microsoft.Extensions.Options;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class GetSubmissionDetailUseCase(
    ITryGetSubmissionQuery getSubmission,
    ITryGetFormVersionQuery getVersion,
    IOptions<EntriqaOptions> options) : IGetSubmissionDetailUseCase
{
    public async Task<SubmissionDetailView> ExecuteAsync(string submissionId, CancellationToken ct = default)
    {
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct);
        var def = v?.Definition.Localize(s.Locale);

        var values = new List<SubmissionValueView>();
        foreach (var (id, value) in s.Values)
        {
            var field = def?.Fields.FirstOrDefault(f => f.Id == id);
            values.Add(new SubmissionValueView(id, field?.Label.ToString() ?? id, value));
        }

        var resultTitle = s.Quiz is null ? null
            : def?.Quiz?.Results.FirstOrDefault(r => r.Id == s.Quiz.ResultId)?.Title.ToString();

        var canResendDoi = !s.IsConfirmed && s.HasEmail
            && def?.Pipeline.Any(st => st.Step == "doi.request") == true
            && s.StepRuns.Any(r => r.Status == StepRunStatus.Waiting);

        List<QuizAnswerView>? quizAnswers = null;
        if (s.Quiz is { } outcome && def?.Quiz is { } quizDef)
        {
            quizAnswers = new List<QuizAnswerView>();
            foreach (var qid in outcome.Path)
            {
                var question = quizDef.Questions.FirstOrDefault(q => q.Id == qid);
                var option = question?.Options.FirstOrDefault(o => o.Id == outcome.Answers.GetValueOrDefault(qid));
                quizAnswers.Add(new QuizAnswerView(
                    question?.Text.ToString() ?? qid,
                    option?.Label.ToString() ?? "–",
                    option?.Points ?? 0,
                    option?.Next?.StartsWith("result:", StringComparison.Ordinal) == true));
            }
        }

        var expiresAt = SubmissionRetention.EffectiveExpiry(s.CreatedAt, s.RetainUntil, options.Value.RetentionDays);
        return new SubmissionDetailView(s.Id, s.Slug, s.Version, s.CreatedAt, s.Locale, s.Email, s.FirstName,
            s.Source, values, s.Quiz, resultTitle, s.ConsentText, s.ConfirmedAt, s.StepRuns,
            s.History.Entries.Reverse().ToList(), s.Handling, s.State,
            s.BrevoContactId, canResendDoi, quizAnswers, s.Assignee,
            expiresAt, expiresAt == DateTimeOffset.MaxValue, s.RetainUntil.HasValue);
    }
}

internal sealed class SetSubmissionHandlingUseCase(
    ITryGetSubmissionQuery getSubmission,
    ISaveSubmissionCommand save,
    TimeProvider time) : ISetSubmissionHandlingUseCase
{
    public async Task ExecuteAsync(string submissionId, string handling, string? by, CancellationToken ct = default)
    {
        if (handling is not (HandlingStates.Open or HandlingStates.Done))
            throw new AppException(ErrorCodes.Validation, ErrorMessages.HandlingInvalid);
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        if (s.Handling == HandlingStates.None)
            throw new AppException(ErrorCodes.Validation, ErrorMessages.HandlingUnsupported);
        s.Handling = handling;
        s.Record(HistoryTypes.Handling, HistoryActor.Admin(by), time.GetUtcNow(), handling);
        await save.ExecuteAsync(s, ct);
    }
}

/// <summary>
/// Retain permanently, extend by a fixed period, or lift the exception (#15) - one field, three ways to
/// change it, mirroring <see cref="SetSubmissionHandlingUseCase"/>.
/// </summary>
internal sealed class SetSubmissionRetentionUseCase(
    ITryGetSubmissionQuery getSubmission,
    ISaveSubmissionCommand save,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : ISetSubmissionRetentionUseCase
{
    /// <summary>The fixed period behind "extend" (comprehension check, #15): a year, not a typed-in date.</summary>
    private const int ExtensionDays = 365;

    public async Task ExecuteAsync(string submissionId, SubmissionRetentionAction action, string? by, CancellationToken ct = default)
    {
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        var now = time.GetUtcNow();
        switch (action)
        {
            case SubmissionRetentionAction.RetainPermanently:
                s.RetainUntil = DateTimeOffset.MaxValue;
                s.Record(HistoryTypes.RetentionRetained, HistoryActor.Admin(by), now);
                break;
            case SubmissionRetentionAction.Extend:
                if (s.RetainUntil == DateTimeOffset.MaxValue)
                    throw new AppException(ErrorCodes.Validation, ErrorMessages.RetentionAlreadyPermanent);
                var current = SubmissionRetention.EffectiveExpiry(s.CreatedAt, s.RetainUntil, options.Value.RetentionDays);
                s.RetainUntil = current.AddDays(ExtensionDays);
                s.Record(HistoryTypes.RetentionExtended, HistoryActor.Admin(by), now, s.RetainUntil.Value.ToString("O"));
                break;
            case SubmissionRetentionAction.Lift:
                s.RetainUntil = null;
                s.Record(HistoryTypes.RetentionLifted, HistoryActor.Admin(by), now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
        await save.ExecuteAsync(s, ct);
    }
}

internal sealed class DeleteSubmissionAdminUseCase(
    ITryGetSubmissionQuery getSubmission,
    IDeleteSubmissionCommand delete,
    IStoreArtifactPort artifacts) : IDeleteSubmissionAdminUseCase
{
    public async Task ExecuteAsync(string submissionId, CancellationToken ct = default)
    {
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        foreach (var path in s.Artifacts.Values.Where(v => !v.Contains("://", StringComparison.Ordinal)))
            await artifacts.DeleteAsync(path, ct);                          // like retention: blobs first
        await delete.ExecuteAsync(s, ct);
    }
}

/// <summary>
/// Assigning, handing over and unassigning are one operation (#13): the submission carries at most
/// one assignee, and setting it to nobody is how it loses one. The boundary is the one
/// <see cref="SetSubmissionHandlingUseCase"/> already draws - a form that tracks no handling has no
/// open state either, so an assignment on it could never surface in a personal filter.
///
/// It knows the submission and how to save it, and nothing else: that is what makes AC7 - an
/// assignment never notifies anyone - true by construction rather than by care.
/// </summary>
internal sealed class SetSubmissionAssigneeUseCase(
    ITryGetSubmissionQuery getSubmission,
    ISaveSubmissionCommand save,
    TimeProvider time) : ISetSubmissionAssigneeUseCase
{
    public async Task ExecuteAsync(string submissionId, string? assignee, string? by, CancellationToken ct = default)
    {
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        if (s.Handling == HandlingStates.None)
            throw new AppException(ErrorCodes.Validation, ErrorMessages.HandlingUnsupported);
        // A <select> sends "" for its "nobody" option and an empty body degrades to the same, so a
        // blank name means nobody rather than an admin whose name is blank.
        s.Assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim();
        s.Record(HistoryTypes.Assignee, HistoryActor.Admin(by), time.GetUtcNow(), s.Assignee);
        await save.ExecuteAsync(s, ct);
    }
}
