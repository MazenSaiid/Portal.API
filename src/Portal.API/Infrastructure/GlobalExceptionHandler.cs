using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Common.Exceptions;

namespace Portal.API.Infrastructure;

/// <summary>Maps application exceptions to RFC 7807 problem responses in one place.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException ex => new ValidationProblemDetails(
                ex.Errors
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException ex => Problem(StatusCodes.Status404NotFound, "Not found", ex.Message),
            ConflictException ex => Problem(StatusCodes.Status409Conflict, "Conflict", ex.Message),
            ForbiddenException ex => Problem(StatusCodes.Status403Forbidden, "Forbidden", ex.Message),
            BusinessRuleException ex => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", ex.Message),
            AuthenticationFailedException ex => Problem(StatusCodes.Status401Unauthorized, "Authentication failed", ex.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
