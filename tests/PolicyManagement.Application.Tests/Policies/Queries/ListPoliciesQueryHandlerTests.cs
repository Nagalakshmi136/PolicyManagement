using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.Common.Models;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Application.Policies.Queries.ListPolicies;
using PolicyManagement.Domain.Common;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Tests.Policies.Queries;

public sealed class ListPoliciesQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();
    private readonly ListPoliciesQueryHandler _sut;

    public ListPoliciesQueryHandlerTests()
    {
        _sut = new ListPoliciesQueryHandler(
            _repositoryMock.Object,
            NullLogger<ListPoliciesQueryHandler>.Instance);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Policy BuildPolicy(
        string policyNumber = "POL-000001",
        string policyholderName = "Tanaka Hiroshi",
        LineOfBusiness lineOfBusiness = LineOfBusiness.Marine,
        PolicyStatus status = PolicyStatus.Active,
        decimal premiumAmount = 125_000m,
        string currency = "JPY",
        string region = "Japan",
        string underwriter = "Chen Wei")
        => Policy.Create(
            policyNumber,
            policyholderName,
            lineOfBusiness,
            status,
            premiumAmount,
            currency,
            new DateOnly(2024, 4, 1),
            new DateOnly(2025, 3, 31),
            region,
            underwriter);

    private static PagedResult<Policy> BuildPagedResult(
        IReadOnlyList<Policy> items,
        int totalCount = -1,
        int page = 1,
        int pageSize = 20)
        => new(items, totalCount < 0 ? items.Count : totalCount, page, pageSize);

    // ------------------------------------------------------------------
    // Happy path — mapped DTOs
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidQuery_ReturnsMappedDtos()
    {
        // Arrange
        var policy = BuildPolicy();
        var pagedResult = BuildPagedResult([policy]);

        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        var query = new ListPoliciesQuery(Page: 1, PageSize: 20);

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);

        var dto = response.Items[0];
        dto.PolicyNumber.Should().Be(policy.PolicyNumber);
        dto.PolicyholderName.Should().Be(policy.PolicyholderName);
        dto.LineOfBusiness.Should().Be("Marine");
        dto.Status.Should().Be("Active");
        dto.PremiumAmount.Should().Be(125_000m);
        dto.Currency.Should().Be("JPY");
        dto.Region.Should().Be("Japan");
        dto.Underwriter.Should().Be("Chen Wei");
        dto.FlaggedForReview.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // AccidentAndHealth maps to "A&H"
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_PolicyWithAccidentAndHealthLob_MapsToApiString()
    {
        // Arrange
        var policy = BuildPolicy(policyNumber: "POL-000002", lineOfBusiness: LineOfBusiness.AccidentAndHealth);
        var pagedResult = BuildPagedResult([policy]);

        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        // Act
        var response = await _sut.Handle(new ListPoliciesQuery(), CancellationToken.None);

        // Assert
        response.Items[0].LineOfBusiness.Should().Be("A&H");
    }

    // ------------------------------------------------------------------
    // Empty result
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_NoMatchingPolicies_ReturnsEmptyDataArray()
    {
        // Arrange
        var pagedResult = BuildPagedResult(Array.Empty<Policy>(), totalCount: 0);

        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        // Act
        var response = await _sut.Handle(new ListPoliciesQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
        response.Pagination.TotalCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Page beyond total — repository returns empty page; handler passes through
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_PageBeyondTotal_ReturnsEmptyDataArrayWithAccuratePagination()
    {
        // Arrange — 15 total records, page 3 of pageSize 10 is out of range
        var pagedResult = BuildPagedResult(Array.Empty<Policy>(), totalCount: 15, page: 3, pageSize: 10);

        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        var query = new ListPoliciesQuery(Page: 3, PageSize: 10);

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
        response.Pagination.TotalCount.Should().Be(15);
        response.Pagination.TotalPages.Should().Be(2);
        response.Pagination.Page.Should().Be(3);
    }

    // ------------------------------------------------------------------
    // Search criteria mapping — Status string forwarded as enum
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithStatusFilter_PassesCorrectEnumToRepository()
    {
        // Arrange
        var pagedResult = BuildPagedResult(Array.Empty<Policy>(), totalCount: 0);

        PolicySearchCriteria? capturedCriteria = null;
        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback<PolicySearchCriteria, CancellationToken>((c, _) => capturedCriteria = c)
            .ReturnsAsync(pagedResult);

        var query = new ListPoliciesQuery(Status: "Expired");

        // Act
        await _sut.Handle(query, CancellationToken.None);

        // Assert
        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Status.Should().Be(PolicyStatus.Expired);
    }

    // ------------------------------------------------------------------
    // Search criteria mapping — "A&H" forwarded as AccidentAndHealth enum
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithAmpersandHLobFilter_PassesAccidentAndHealthEnumToRepository()
    {
        // Arrange
        var pagedResult = BuildPagedResult(Array.Empty<Policy>(), totalCount: 0);

        PolicySearchCriteria? capturedCriteria = null;
        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback<PolicySearchCriteria, CancellationToken>((c, _) => capturedCriteria = c)
            .ReturnsAsync(pagedResult);

        var query = new ListPoliciesQuery(LineOfBusiness: "A&H");

        // Act
        await _sut.Handle(query, CancellationToken.None);

        // Assert
        capturedCriteria!.LineOfBusiness.Should().Be(LineOfBusiness.AccidentAndHealth);
    }

    // ------------------------------------------------------------------
    // SortDirection "desc" maps to SortDescending = true
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithDescSortDirection_SetsSortDescendingTrue()
    {
        // Arrange
        var pagedResult = BuildPagedResult(Array.Empty<Policy>(), totalCount: 0);

        PolicySearchCriteria? capturedCriteria = null;
        _repositoryMock
            .Setup(r => r.SearchAsync(It.IsAny<PolicySearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback<PolicySearchCriteria, CancellationToken>((c, _) => capturedCriteria = c)
            .ReturnsAsync(pagedResult);

        var query = new ListPoliciesQuery(SortBy: "premiumAmount", SortDirection: "desc");

        // Act
        await _sut.Handle(query, CancellationToken.None);

        // Assert
        capturedCriteria!.SortDescending.Should().BeTrue();
        capturedCriteria.SortBy.Should().Be("premiumAmount");
    }
}
