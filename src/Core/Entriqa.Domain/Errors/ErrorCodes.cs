namespace Entriqa.Domain.Errors;

public static class ErrorCodes
{
    public const string Validation = "forms.validation";
    public const string FormNotFound = "forms.not_found";
    public const string FormNotPublished = "forms.not_published";
    public const string SubmissionNotFound = "submissions.not_found";
    public const string SpamRejected = "submissions.spam_rejected";
    public const string TokenInvalid = "security.token_invalid";
    public const string TokenExpired = "security.token_expired";
    public const string TokenTooEarly = "security.token_too_early";
    public const string TokenReplayed = "security.token_replayed";
    public const string RateLimited = "security.rate_limited";
    public const string Forbidden = "security.forbidden";
    public const string QuizPathLoop = "quiz.path_loop";
    public const string QuizInvalidDefinition = "quiz.invalid_definition";
    public const string Conflict = "submissions.conflict";
    public const string StepUnknown = "pipeline.step_unknown";
    public const string StepConfigInvalid = "pipeline.step_config_invalid";
    public const string Infrastructure = "infrastructure.error";
    public const string AppointmentNotFound = "appointments.not_found";
}
