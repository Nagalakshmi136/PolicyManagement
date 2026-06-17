using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace PolicyManagement.Application.Common.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that logs the start, successful completion,
/// and failure of every request. Registered as the outermost behaviour so
/// it measures the total elapsed time including validation.
/// Never logs request payloads to avoid leaking sensitive data.
/// </summary>
internal sealed class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation("Handling {RequestName}", requestName);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken);
            sw.Stop();

            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName,
                sw.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();

            logger.LogWarning(
                ex,
                "Request {RequestName} failed after {ElapsedMs}ms",
                requestName,
                sw.ElapsedMilliseconds);

            throw; // always rethrow — behaviours must not swallow exceptions
        }
    }
}
