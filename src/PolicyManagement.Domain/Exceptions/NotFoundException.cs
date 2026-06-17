namespace PolicyManagement.Domain.Exceptions;

/// <summary>
/// Thrown when a requested entity cannot be found. Maps to HTTP 404 Not Found
/// via <c>ExceptionHandlingMiddleware</c>.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.") { }
}
