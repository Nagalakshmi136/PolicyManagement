namespace PolicyManagement.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant is violated. Maps to HTTP 422 Unprocessable
/// Entity via <c>ExceptionHandlingMiddleware</c>.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
