using Flenta.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Flenta.Application.Features.Auth;

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
        var applicationFailure = exception as ApplicationFailureException;
        var status = applicationFailure?.Kind switch
        {
            FailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
            FailureKind.Forbidden => StatusCodes.Status403Forbidden,
            FailureKind.Conflict => StatusCodes.Status409Conflict,
            FailureKind.RateLimited => StatusCodes.Status429TooManyRequests,
            FailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            FailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
            _ => validationException is not null ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = applicationFailure is not null ? applicationFailure.Message
                : validationException is not null ? "Validation failed." : "An unexpected error occurred.",
            Detail = validationException?.Message,
            Instance = httpContext.Request.Path
        };

        if (validationException is not null)
        {
            problem.Extensions["code"] = validationException.Code;
        }
        if (applicationFailure is not null) problem.Extensions["code"] = applicationFailure.Code;

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
