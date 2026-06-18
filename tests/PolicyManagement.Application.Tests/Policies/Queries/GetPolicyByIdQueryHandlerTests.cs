using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Application.Policies.Queries.GetPolicyById;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Tests.Policies.Queries;

public sealed class GetPolicyByIdQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();
    private readonly GetPolicyByIdQueryHandler _sut;

    public GetPolicyByIdQueryHandlerTests()
    {
        _sut = new GetPolicyByIdQueryHandler(
            _repositoryMock.Object,
            NullLogger<GetPolicyByIdQueryHandler>.Instance);
    }

    // ------------------------------------------------------------------
    // Helper
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

    // ------------------------------------------------------------------
    // Happy path — all fields mapped correctly
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_ExistingId_ReturnsMappedDto()
    {
        // Arrange
        var policy = BuildPolicy();
        var id = policy.Id;

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var dto = await _sut.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(policy.Id);
        dto.PolicyNumber.Should().Be("POL-000001");
        dto.PolicyholderName.Should().Be("Tanaka Hiroshi");
        dto.LineOfBusiness.Should().Be("Marine");
        dto.Status.Should().Be("Active");
        dto.PremiumAmount.Should().Be(125_000m);
        dto.Currency.Should().Be("JPY");
        dto.EffectiveDate.Should().Be(new DateOnly(2024, 4, 1));
        dto.ExpiryDate.Should().Be(new DateOnly(2025, 3, 31));
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
        var policy = BuildPolicy(
            policyNumber: "POL-000002",
            lineOfBusiness: LineOfBusiness.AccidentAndHealth);
        var id = policy.Id;

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var dto = await _sut.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        dto.LineOfBusiness.Should().Be("A&H");
    }

    // ------------------------------------------------------------------
    // FlaggedForReview reflected in DTO
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_FlaggedPolicy_ReturnsDto_WithFlaggedForReviewTrue()
    {
        // Arrange
        var policy = BuildPolicy(policyNumber: "POL-000003");
        policy.FlagForReview();
        var id = policy.Id;

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var dto = await _sut.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        dto.FlaggedForReview.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // All LineOfBusiness enum values map correctly
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(LineOfBusiness.Property, "Property")]
    [InlineData(LineOfBusiness.Casualty, "Casualty")]
    [InlineData(LineOfBusiness.AccidentAndHealth, "A&H")]
    [InlineData(LineOfBusiness.Marine, "Marine")]
    public async Task Handle_LineOfBusinessEnumValues_MapToCorrectApiStrings(
        LineOfBusiness lob, string expectedApiString)
    {
        // Arrange
        var policy = BuildPolicy(policyNumber: "POL-000004", lineOfBusiness: lob);
        var id = policy.Id;

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var dto = await _sut.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        dto.LineOfBusiness.Should().Be(expectedApiString);
    }

    // ------------------------------------------------------------------
    // All PolicyStatus enum values map correctly
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(PolicyStatus.Active, "Active")]
    [InlineData(PolicyStatus.Expired, "Expired")]
    [InlineData(PolicyStatus.Pending, "Pending")]
    [InlineData(PolicyStatus.Cancelled, "Cancelled")]
    public async Task Handle_PolicyStatusEnumValues_MapToCorrectApiStrings(
        PolicyStatus status, string expectedApiString)
    {
        // Arrange
        var policy = BuildPolicy(policyNumber: "POL-000005", status: status);
        var id = policy.Id;

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var dto = await _sut.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        dto.Status.Should().Be(expectedApiString);
    }

    // ------------------------------------------------------------------
    // Policy not found → NotFoundException
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        // Arrange
        var missingId = Guid.NewGuid().ToString();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Policy?)null);

        // Act
        var act = async () => await _sut.Handle(new GetPolicyByIdQuery(missingId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Policy*");
    }

    // ------------------------------------------------------------------
    // Repository is called with the exact id from the query
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_CallsRepositoryWithCorrectId()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Policy?)null);

        // Act
        var act = async () => await _sut.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();

        // Assert
        _repositoryMock.Verify(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
