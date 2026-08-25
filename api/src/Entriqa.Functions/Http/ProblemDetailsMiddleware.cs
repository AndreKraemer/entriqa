using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Entriqa.Domain.Errors;

namespace Entriqa.Functions.Http;

/// <summary>The boundary of the error model (§11.5): AppException -> ProblemDetails with an ErrorCode; everything else -> 500 without details.</summary>
public sealed class ProblemDetailsMiddleware(ILogger<ProblemDetailsMiddleware> log) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            var app = ex as AppException ?? ex.InnerException as AppException;
            var problem = new ProblemDetails
            {
                Status = app?.HttpStatus ?? 500,
                Title = app?.Message ?? "Interner Fehler.",
                Type = "https://errors.entriqa/" + (app?.ErrorCode ?? ErrorCodes.Infrastructure),
                Extensions = { ["errorCode"] = app?.ErrorCode ?? ErrorCodes.Infrastructure, ["traceId"] = context.InvocationId },
            };
            if (app is ValidationException v) problem.Extensions["errors"] = v.Errors.Select(e => new { e.Field, e.Message }).ToList();
            if (app is null || app.HttpStatus >= 500) log.LogError(ex, "Unbehandelter Fehler in {Function}", context.FunctionDefinition.Name);
            else log.LogInformation("{ErrorCode}: {Message}", app.ErrorCode, app.Message);

            // ASP.NET Core integration: write into the response directly; GetInvocationResult is not the documented way here.
            var http = context.GetHttpContext();
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
