using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Domain.Errors;

namespace Entriqa.Functions.Http;

/// <summary>
/// The boundary of the error model (§11.5): AppException -> ProblemDetails with an ErrorCode; everything else -> 500 without details.
/// This is also where the text gets its language: the throw site carries a key, here it is rendered in the language
/// of the request (see <see cref="RequestLocale"/>). Server-side failures answer with a generic text - their detail
/// belongs in the log, not in the response.
/// </summary>
public sealed class ProblemDetailsMiddleware(ILogger<ProblemDetailsMiddleware> log, IOptions<EntriqaOptions> options) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            var app = ex as AppException ?? ex.InnerException as AppException;
            var http = context.GetHttpContext();
            var lang = RequestLocale.From(http?.Request, options.Value);
            var status = app?.HttpStatus ?? 500;
            var errorCode = app?.ErrorCode ?? ErrorCodes.Infrastructure;
            var title = status >= 500 || app is null
                ? ErrorMessages.Get(lang, ErrorMessages.Internal)
                : app.Localize(lang);

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = "https://errors.entriqa/" + errorCode,
                Extensions = { ["errorCode"] = errorCode, ["traceId"] = context.InvocationId },
            };
            if (app is ValidationException v) problem.Extensions["errors"] = v.Errors.Select(e => new { e.Field, e.Message }).ToList();
            if (app is null || app.HttpStatus >= 500) log.LogError(ex, "Unbehandelter Fehler in {Function}", context.FunctionDefinition.Name);
            else log.LogInformation("{ErrorCode}: {Message}", app.ErrorCode, app.Message);

            // ASP.NET Core integration: write into the response directly; GetInvocationResult is not the documented way here.
            if (http is not null && !http.Response.HasStarted)
            {
                http.Response.StatusCode = problem.Status ?? 500;
                await http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.CancellationToken);
            }
            else
            {
                context.GetInvocationResult().Value = new ObjectResult(problem) { StatusCode = problem.Status };
            }
        }
    }
}
