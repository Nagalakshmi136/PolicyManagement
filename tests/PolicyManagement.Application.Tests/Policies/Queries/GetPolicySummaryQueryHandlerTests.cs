using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PolicyManagement.Application.Common.Options;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Application.Policies.Queries.GetPolicySummary;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Tests.Policies.Queries;

public sealed class GetPolicySummaryQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();

    private GetPolicySummaryQueryHandler BuildSut(int expiringSoonDays = 30)
    {
        var options = Options.Create(new CacheOptions { ExpiringSoonDays = expiringSoonDays });
        return new GetPolicySummaryQueryHandler(
            _repositoryMock.Object,
            options,
            NullLogger<GetPolicySummaryQueryHandler>.Instance);
    }

    // ------------------------------------------------------------------
    // Helper — builds a PolicySummaryData with configurable data
    // ------------------------------------------------------------------

    private static PolicySummaryData BuildSummaryData(
        int active = 0,
        int expired = 0,
        int pending = 0,
        int cancelled = 0,
        IReadOnlyDictionary<(LineOfBusiness, string), decimal>? premiumByLobAndCurrency = null,
        int expiringSoonCount = 0)
        => new(
            TotalCount: active + expired + pending + cancelled,
            ActiveCount: active,
            ExpiredCount: expired,
            PendingCount: pending,
            CancelledCount: cancelled,
            PremiumByLobAndCurrency: premiumByLobAndCurrency
                ?? new Dictionary<(LineOfBusiness, string), decimal>(),
            ExpiringSoonCount: expiringSoonCount);

    // ------------------------------------------------------------------
    // Status counts match expected distribution
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsSummary_WithCorrectStatusCounts()
    {
        // Arrange
        var summaryData = BuildSummaryData(active: 98, expired: 34, pending: 12, cancelled: 7);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.CountsByStatus.Active.Should().Be(98);
        result.CountsByStatus.Expired.Should().Be(34);
        result.CountsByStatus.Pending.Should().Be(12);
        result.CountsByStatus.Cancelled.Should().Be(7);
    }

    [Fact]
    public async Task Handle_WhenNoPoliciesExist_ReturnsAllZeroCounts()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSummaryData());

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.CountsByStatus.Active.Should().Be(0);
        result.CountsByStatus.Expired.Should().Be(0);
        result.CountsByStatus.Pending.Should().Be(0);
        result.CountsByStatus.Cancelled.Should().Be(0);
        result.TotalPremiumByLineOfBusiness.Should().BeEmpty();
        result.ExpiringSoonCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Premium grouped by lineOfBusiness AND currency — not lineOfBusiness alone
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsPremium_GroupedByLobAndCurrency_NotLobAlone()
    {
        // Arrange — Marine has two currencies; Property has one
        var premiumData = new Dictionary<(LineOfBusiness, string), decimal>
        {
            { (LineOfBusiness.Marine,   "USD"), 1_250_000m },
            { (LineOfBusiness.Marine,   "SGD"),   430_000m },
            { (LineOfBusiness.Property, "USD"), 3_100_000m },
        };

        var summaryData = BuildSummaryData(premiumByLobAndCurrency: premiumData);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — three rows, NOT two (Marine is NOT collapsed into one row)
        result.TotalPremiumByLineOfBusiness.Should().HaveCount(3);

        result.TotalPremiumByLineOfBusiness.Should().ContainSingle(x =>
            x.LineOfBusiness == "Marine" && x.Currency == "USD" && x.TotalPremium == 1_250_000m);

        result.TotalPremiumByLineOfBusiness.Should().ContainSingle(x =>
            x.LineOfBusiness == "Marine" && x.Currency == "SGD" && x.TotalPremium == 430_000m);

        result.TotalPremiumByLineOfBusiness.Should().ContainSingle(x =>
            x.LineOfBusiness == "Property" && x.Currency == "USD" && x.TotalPremium == 3_100_000m);
    }

    [Fact]
    public async Task Handle_DoesNotCollapseMultipleCurrencies_ForSameLob()
    {
        // Arrange — one LoB, six different currencies — must produce 6 rows
        var premiumData = new Dictionary<(LineOfBusiness, string), decimal>
        {
            { (LineOfBusiness.Casualty, "USD"), 100_000m },
            { (LineOfBusiness.Casualty, "SGD"), 200_000m },
            { (LineOfBusiness.Casualty, "HKD"), 300_000m },
            { (LineOfBusiness.Casualty, "AUD"), 400_000m },
            { (LineOfBusiness.Casualty, "JPY"), 500_000m },
            { (LineOfBusiness.Casualty, "THB"), 600_000m },
        };

        var summaryData = BuildSummaryData(premiumByLobAndCurrency: premiumData);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — 6 distinct currency rows, not 1 collapsed total
        result.TotalPremiumByLineOfBusiness.Should().HaveCount(6);
        result.TotalPremiumByLineOfBusiness
            .Where(x => x.LineOfBusiness == "Casualty")
            .Should().HaveCount(6);
    }

    [Fact]
    public async Task Handle_AccidentAndHealthLob_MapsToApiString()
    {
        // Arrange
        var premiumData = new Dictionary<(LineOfBusiness, string), decimal>
        {
            { (LineOfBusiness.AccidentAndHealth, "SGD"), 750_000m },
        };

        var summaryData = BuildSummaryData(premiumByLobAndCurrency: premiumData);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.TotalPremiumByLineOfBusiness.Should().ContainSingle(x =>
            x.LineOfBusiness == "A&H" && x.Currency == "SGD" && x.TotalPremium == 750_000m);
    }

    // ------------------------------------------------------------------
    // ExpiringSoonCount — uses 30-day window from today (default)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_PassesCutoffDateToRepository_UsingExpiringSoonDaysFromOptions()
    {
        // Arrange
        const int configuredDays = 30;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedCutoff = today.AddDays(configuredDays);

        DateOnly? capturedCutoff = null;

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((cutoff, _) => capturedCutoff = cutoff)
            .ReturnsAsync(BuildSummaryData());

        // Act
        await BuildSut(expiringSoonDays: configuredDays).Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — cutoff is today + 30 days (allow ±1 day tolerance for clock drift between test start and handler execution)
        capturedCutoff.Should().NotBeNull();
        capturedCutoff!.Value.Should().BeOnOrAfter(expectedCutoff.AddDays(-1));
        capturedCutoff.Value.Should().BeOnOrBefore(expectedCutoff.AddDays(1));
    }

    [Fact]
    public async Task Handle_ConfiguredWith7Days_PassesCutoffAs7DaysFromToday()
    {
        // Arrange
        const int configuredDays = 7;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedCutoff = today.AddDays(configuredDays);

        DateOnly? capturedCutoff = null;

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((cutoff, _) => capturedCutoff = cutoff)
            .ReturnsAsync(BuildSummaryData());

        // Act
        await BuildSut(expiringSoonDays: configuredDays).Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        capturedCutoff!.Value.Should().Be(expectedCutoff);
    }

    [Fact]
    public async Task Handle_ExpiringSoonCount_ReflectsRepositoryValue()
    {
        // Arrange
        var summaryData = BuildSummaryData(active: 10, expiringSoonCount: 5);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.ExpiringSoonCount.Should().Be(5);
    }

    [Fact]
    public async Task Handle_ExpiringSoonCountZero_WhenNoPoliciesExpiringSoon()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSummaryData(active: 20, expiringSoonCount: 0));

        // Act
        var result = await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.ExpiringSoonCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Repository is called exactly once per query
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_CallsGetSummaryAsyncExactlyOnce()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSummaryData());

        // Act
        await BuildSut().Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.GetSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
