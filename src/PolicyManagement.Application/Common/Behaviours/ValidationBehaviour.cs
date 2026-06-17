using FluentValidation;
using MediatR;

namespace PolicyManagement.Application.Common.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that runs all registered FluentValidation
/// validators for <typeparamref name="TRequest"/> before the handler is invoked.
/// Throws <see cref="ValidationException"/> on the first set of failures,
/// which <c>ExceptionHandlingMiddleware</c> maps to HTTP 400 with an errors map.
/// </summary>
internal sealed class ValidationBehaviour<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}
