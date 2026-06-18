using Serilog.Context;

namespace PolicyManagement.Api.Middleware;

/// <summary>
/// Reads the <c>X-Correlation-Id</c> request header (or generates a new UUID if absent),
/// writes it back to the response header, and pushes it into the Serilog
/// <see cref="LogContext"/> so all log entries within the request carry the property.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
