using Flenta.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Flenta.Api.Middleware;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var validationException = exception as DomainValidationException;
        var status = validationException is null
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status400BadRequest;

        if (validationException is null)
        {
            logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = validationException is null ? "An unexpected error occurred." : "Validation failed.",
            Detail = validationException?.Message,
            Instance = httpContext.Request.Path
        };

        if (validationException is not null)
        {
            problem.Extensions["code"] = validationException.Code;
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
