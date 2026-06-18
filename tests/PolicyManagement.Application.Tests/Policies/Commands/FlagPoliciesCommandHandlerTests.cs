using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.Policies.Commands.FlagPolicies;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Tests.Policies.Commands;

public sealed class FlagPoliciesCommandHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly FlagPoliciesCommandHandler _sut;

    public FlagPoliciesCommandHandlerTests()
    {
        _sut = new FlagPoliciesCommandHandler(
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            NullLogger<FlagPoliciesCommandHandler>.Instance);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Policy BuildPolicy(string policyNumber = "POL-000001")
        => Policy.Create(
            policyNumber,
            "Tanaka Hiroshi",
            LineOfBusiness.Marine,
            PolicyStatus.Active,
            125_000m,
            "JPY",
            new DateOnly(2024, 4, 1),
            new DateOnly(2025, 3, 31),
            "Japan",
            "Chen Wei");

    private void SetupRepository(IEnumerable<Policy> policies)
        => _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(policies.ToList().AsReadOnly());

    // ------------------------------------------------------------------
    // All IDs exist — all flagged, notFoundIds empty
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_AllIdsExist_ReturnsAllFlaggedWithEmptyNotFoundIds()
    {
        // Arrange
        var p1 = BuildPolicy("POL-000001");
        var p2 = BuildPolicy("POL-000002");
        SetupRepository([p1, p2]);

        var command = new FlagPoliciesCommand([p1.Id, p2.Id]);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.FlaggedCount.Should().Be(2);
        result.FlaggedIds.Should().BeEquivalentTo([p1.Id, p2.Id]);
        result.NotFoundIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AllIdsExist_PolicyFlaggedForReviewIsTrue()
    {
        // Arrange
        var policy = BuildPolicy("POL-000003");
        policy.FlaggedForReview.Should().BeFalse();
        SetupRepository([policy]);

        // Act
        await _sut.Handle(new FlagPoliciesCommand([policy.Id]), CancellationToken.None);

        // Assert
        policy.FlaggedForReview.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AllIdsExist_SaveChangesCalledOnce()
    {
        // Arrange
        var policy = BuildPolicy("POL-000004");
        SetupRepository([policy]);

        // Act
        await _sut.Handle(new FlagPoliciesCommand([policy.Id]), CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // Mixed IDs — partial success, notFoundIds populated
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_MixedIds_ReturnsFlaggedAndNotFoundIds()
    {
        // Arrange
        var existingPolicy = BuildPolicy("POL-000005");
        var missingId = Guid.NewGuid().ToString();

        // Repository only returns the one that exists
        SetupRepository([existingPolicy]);

        var command = new FlagPoliciesCommand([existingPolicy.Id, missingId]);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.FlaggedCount.Should().Be(1);
        result.FlaggedIds.Should().ContainSingle(id => id == existingPolicy.Id);
        result.NotFoundIds.Should().ContainSingle(id => id == missingId);
    }

    [Fact]
    public async Task Handle_MixedIds_FlaggedCountMatchesFlaggedIdsLength()
    {
        // Arrange
        var p1 = BuildPolicy("POL-000006");
        var p2 = BuildPolicy("POL-000007");
        var missing1 = Guid.NewGuid().ToString();
        var missing2 = Guid.NewGuid().ToString();

        SetupRepository([p1, p2]);

        var command = new FlagPoliciesCommand([p1.Id, p2.Id, missing1, missing2]);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.FlaggedCount.Should().Be(2);
        result.FlaggedIds.Should().HaveCount(result.FlaggedCount);
        result.NotFoundIds.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------
    // All IDs unknown — 200 with flaggedCount 0, all in notFoundIds
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_AllIdsUnknown_Returns200WithFlaggedCountZero()
    {
        // Arrange
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        SetupRepository([]);   // repository returns nothing

        var command = new FlagPoliciesCommand([id1, id2]);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert — no exception, 200-equivalent response
        result.FlaggedCount.Should().Be(0);
        result.FlaggedIds.Should().BeEmpty();
        result.NotFoundIds.Should().BeEquivalentTo([id1, id2]);
    }

    [Fact]
    public async Task Handle_AllIdsUnknown_SaveChangesNotCalled()
    {
        // Arrange
        SetupRepository([]);

        var command = new FlagPoliciesCommand([Guid.NewGuid().ToString()]);

        // Act
        await _sut.Handle(command, CancellationToken.None);

        // Assert — no mutations, no commit needed
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Idempotency — already-flagged policy still appears in flaggedIds
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_AlreadyFlaggedPolicy_IsIdempotentAndAppearsInFlaggedIds()
    {
        // Arrange
        var policy = BuildPolicy("POL-000008");
        policy.FlagForReview();  // pre-flag via domain method
        policy.FlaggedForReview.Should().BeTrue();

        SetupRepository([policy]);

        var command = new FlagPoliciesCommand([policy.Id]);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert — still counted as flagged, not as not-found
        result.FlaggedCount.Should().Be(1);
        result.FlaggedIds.Should().ContainSingle(id => id == policy.Id);
        result.NotFoundIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AlreadyFlaggedPolicy_FlaggedForReviewRemainsTrue()
    {
        // Arrange
        var policy = BuildPolicy("POL-000009");
        policy.FlagForReview();
        SetupRepository([policy]);

        // Act
        await _sut.Handle(new FlagPoliciesCommand([policy.Id]), CancellationToken.None);

        // Assert — domain method is idempotent; still true, no state corruption
        policy.FlaggedForReview.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Single ID — minimal happy-path sanity check
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_SingleExistingId_ReturnsFlaggedCountOne()
    {
        // Arrange
        var policy = BuildPolicy("POL-000010");
        SetupRepository([policy]);

        // Act
        var result = await _sut.Handle(new FlagPoliciesCommand([policy.Id]), CancellationToken.None);

        // Assert
        result.FlaggedCount.Should().Be(1);
        result.NotFoundIds.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Repository receives the exact IDs from the command
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_PassesExactIdsToRepository()
    {
        // Arrange
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        IEnumerable<string>? capturedIds = null;
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((ids, _) => capturedIds = ids.ToList())
            .ReturnsAsync(new List<Policy>().AsReadOnly());

        var command = new FlagPoliciesCommand([id1, id2]);

        // Act
        await _sut.Handle(command, CancellationToken.None);

        // Assert
        capturedIds.Should().NotBeNull();
        capturedIds.Should().BeEquivalentTo([id1, id2]);
    }
}
