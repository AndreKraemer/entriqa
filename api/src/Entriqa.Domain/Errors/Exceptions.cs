namespace Entriqa.Domain.Errors;

/// <summary>
/// Error model per Solution Standard §11: typed exceptions carrying an ErrorCode, surfaced as ProblemDetails at the edge.
/// The text is not baked in at the throw site but carried as a key into <see cref="ErrorMessages"/> plus its arguments,
/// so the edge can render it in the language of the request. <see cref="Exception.Message"/> stays German - it goes
/// into the log, where a single language is what you want.
/// </summary>
public class AppException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatus { get; }
    public string MessageKey { get; }
    public IReadOnlyDictionary<string, string>? MessageArgs { get; }

    public AppException(string errorCode, string messageKey, IReadOnlyDictionary<string, string>? messageArgs = null,
                        int httpStatus = 400, Exception? inner = null)
        : base(ErrorMessages.Get(null, messageKey, messageArgs), inner)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
        MessageKey = messageKey;
        MessageArgs = messageArgs;
    }

    /// <summary>The text in the language of the caller; unknown language falls back to German.</summary>
    public string Localize(string? lang) => ErrorMessages.Get(lang, MessageKey, MessageArgs);

    /// <summary>Argument bag for the {placeholders} of a message.</summary>
    public static IReadOnlyDictionary<string, string> Args(string name, string value) =>
        new Dictionary<string, string> { [name] = value };
}

public sealed record FieldError(string Field, string Message);

public sealed class ValidationException : AppException
{
    public IReadOnlyList<FieldError> Errors { get; }
    public ValidationException(IReadOnlyList<FieldError> errors)
        : base(ErrorCodes.Validation, ErrorMessages.ValidationFailed) => Errors = errors;
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string errorCode, string messageKey, IReadOnlyDictionary<string, string>? messageArgs = null)
        : base(errorCode, messageKey, messageArgs, 404) { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string messageKey = ErrorMessages.Forbidden, IReadOnlyDictionary<string, string>? messageArgs = null)
        : base(ErrorCodes.Forbidden, messageKey, messageArgs, 403) { }
}

public sealed class SecurityTokenException : AppException
{
    public SecurityTokenException(string errorCode, string messageKey)
        : base(errorCode, messageKey, null, 400) { }
}

/// <summary>
/// Trouble with a third party or with storage: the detail is for the log, never for the caller.
/// The edge answers a 500 with a generic, localized text (see ProblemDetailsMiddleware).
/// </summary>
public sealed class InfrastructureException : AppException
{
    public string Detail { get; }
    public InfrastructureException(string detail, Exception? inner = null)
        : base(ErrorCodes.Infrastructure, ErrorMessages.Internal, null, 500, inner) => Detail = detail;

    public override string ToString() => Detail + Environment.NewLine + base.ToString();
}
