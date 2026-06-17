using PolicyManagement.Domain.Enumerations;

namespace PolicyManagement.Domain.Repositories;

/// <summary>
/// Encapsulates all filter, sort, and pagination parameters for the
/// <see cref="IPolicyRepository.SearchAsync"/> method.
/// </summary>
public sealed record PolicySearchCriteria(
    int Page,
    int PageSize,
    PolicyStatus? Status = null,
    LineOfBusiness? LineOfBusiness = null,
    string? Region = null,
    string? SearchTerm = null,
    string? SortBy = null,
    bool SortDescending = false,
    DateOnly? EffectiveDateFrom = null,
    DateOnly? EffectiveDateTo = null);
