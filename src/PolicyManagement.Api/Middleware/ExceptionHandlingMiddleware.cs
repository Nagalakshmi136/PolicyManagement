using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PolicyManagement.Domain.Exceptions;

namespace PolicyManagement.Api.Middleware;

/// <summary>
/// Outermost middleware. Catches all unhandled exceptions and converts them to
/// RFC 9457 <see cref="ProblemDetails"/> (<c>application/problem+json</c>).
/// Controllers and handlers must never contain try/catch for expected errors.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var level = ex is NotFoundException or DomainException or ValidationException
                ? LogLevel.Warning
                : LogLevel.Error;

            logger.Log(level, ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail) = exception switch
        {
            ValidationException             => (400, "Bad Request",            "One or more validation errors occurred."),
            NotFoundException notFound      => (404, "Not Found",              notFound.Message),
            DomainException domain          => (422, "Unprocessable Entity",   domain.Message),
            UnauthorizedAccessException _   => (403, "Forbidden",              "You do not have permission to perform this action."),
            _                               => (500, "Internal Server Error",  (string?)null)  // never leak detail on 500
        };

        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/problem+json";

        if (exception is ValidationException validationEx)
        {
            var validationProblem = new ValidationProblemDetails(
                validationEx.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Type     = $"https://tools.ietf.org/html/rfc9110#section-15.5.{statusCode - 399}",
                Title    = title,
                Status   = statusCode,
                Detail   = detail,
                Instance = context.Request.Path
            };
            return context.Response.WriteAsJsonAsync(validationProblem);
        }

        var problem = new ProblemDetails
        {
            Type     = $"https://tools.ietf.org/html/rfc9110#section-15.5.{statusCode - 399}",
            Title    = title,
            Status   = statusCode,
            Detail   = detail,
            Instance = context.Request.Path
        };

        return context.Response.WriteAsJsonAsync(problem);
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
