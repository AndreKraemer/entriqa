using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class GetSubmissionDetailUseCase(
    ITryGetSubmissionQuery getSubmission,
    ITryGetFormVersionQuery getVersion) : IGetSubmissionDetailUseCase
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

        return new SubmissionDetailView(s.Id, s.Slug, s.Version, s.CreatedAt, s.Locale, s.Email, s.FirstName,
            s.Source, values, s.Quiz, resultTitle, s.ConsentText, s.ConfirmedAt, s.StepRuns, s.Handling, s.State,
            s.BrevoContactId, canResendDoi, quizAnswers);
    }
}

internal sealed class SetSubmissionHandlingUseCase(
    ITryGetSubmissionQuery getSubmission,
    ISaveSubmissionCommand save) : ISetSubmissionHandlingUseCase
{
    public async Task ExecuteAsync(string submissionId, string handling, CancellationToken ct = default)
    {
        if (handling is not (HandlingStates.Open or HandlingStates.Done))
            throw new AppException(ErrorCodes.Validation, ErrorMessages.HandlingInvalid);
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        if (s.Handling == HandlingStates.None)
            throw new AppException(ErrorCodes.Validation, ErrorMessages.HandlingUnsupported);
        s.Handling = handling;
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
