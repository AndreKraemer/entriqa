namespace Entriqa.Domain.Errors;

/// <summary>Fehlermodell nach Solution Standard §11: typisierte Exceptions mit ErrorCode, am Rand als ProblemDetails.</summary>
public class AppException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatus { get; }
    public AppException(string errorCode, string message, int httpStatus = 400, Exception? inner = null) : base(message, inner)
    { ErrorCode = errorCode; HttpStatus = httpStatus; }
}

public sealed record FieldError(string Field, string Message);

public sealed class ValidationException : AppException
{
    public IReadOnlyList<FieldError> Errors { get; }
    public ValidationException(IReadOnlyList<FieldError> errors)
        : base(ErrorCodes.Validation, "Eingaben sind ungültig.", 400) => Errors = errors;
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string errorCode, string message) : base(errorCode, message, 404) { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message = "Keine Berechtigung.") : base(ErrorCodes.Forbidden, message, 403) { }
}

public sealed class SecurityTokenException : AppException
{
    public SecurityTokenException(string errorCode, string message) : base(errorCode, message, 400) { }
}

public sealed class InfrastructureException : AppException
{
    public InfrastructureException(string message, Exception? inner = null) : base(ErrorCodes.Infrastructure, message, 500, inner) { }
}
