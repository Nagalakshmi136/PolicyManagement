using FluentAssertions;
using PolicyManagement.Domain.Common;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Events;
using PolicyManagement.Domain.Exceptions;

namespace PolicyManagement.Domain.Tests.Entities;

public class PolicyTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Policy CreateValidPolicy(
        string policyNumber = "POL-123456",
        string policyholderName = "Tan Wei Ming",
        LineOfBusiness lob = LineOfBusiness.Property,
        PolicyStatus status = PolicyStatus.Active,
        decimal premiumAmount = 10_000m,
        string currency = "SGD",
        DateOnly? effectiveDate = null,
        DateOnly? expiryDate = null,
        string region = "Singapore",
        string underwriter = "Alice Teo")
    {
        var start = effectiveDate ?? new DateOnly(2025, 1, 1);
        var end = expiryDate ?? start.AddYears(1);
        return Policy.Create(policyNumber, policyholderName, lob, status,
            premiumAmount, currency, start, end, region, underwriter);
    }

    // ------------------------------------------------------------------
    // Policy.Create — happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Create_WithValidArguments_ShouldReturnPolicyWithExpectedValues()
    {
        // Arrange
        var effective = new DateOnly(2025, 1, 1);
        var expiry = new DateOnly(2026, 1, 1);

        // Act
        var policy = Policy.Create(
            "POL-000001", "Lim Ah Kow", LineOfBusiness.Marine, PolicyStatus.Active,
            50_000m, "USD", effective, expiry, "Singapore", "Ben Chan");

        // Assert
        policy.PolicyNumber.Should().Be("POL-000001");
        policy.PolicyholderName.Should().Be("Lim Ah Kow");
        policy.LineOfBusiness.Should().Be(LineOfBusiness.Marine);
        policy.Status.Should().Be(PolicyStatus.Active);
        policy.PremiumAmount.Should().Be(50_000m);
        policy.Currency.Should().Be("USD");
        policy.EffectiveDate.Should().Be(effective);
        policy.ExpiryDate.Should().Be(expiry);
        policy.Region.Should().Be("Singapore");
        policy.Underwriter.Should().Be("Ben Chan");
        policy.FlaggedForReview.Should().BeFalse();
        policy.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_ShouldAssignUniqueId_EachTime()
    {
        // Act
        var p1 = CreateValidPolicy();
        var p2 = CreateValidPolicy();

        // Assert
        p1.Id.Should().NotBe(p2.Id);
    }

    // ------------------------------------------------------------------
    // Policy.Create — invariant violations
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pol-123456")]    // lowercase
    [InlineData("POL-12345")]     // 5 digits
    [InlineData("POL-1234567")]   // 7 digits
    [InlineData("POL123456")]     // missing dash
    [InlineData("XYZ-123456")]    // wrong prefix
    public void Create_WithInvalidPolicyNumber_ShouldThrowDomainException(string policyNumber)
    {
        // Act
        var act = () => CreateValidPolicy(policyNumber: policyNumber);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*POL-XXXXXX*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyPolicyholderName_ShouldThrowDomainException(string name)
    {
        // Act
        var act = () => CreateValidPolicy(policyholderName: name);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(999.99)]          // just below minimum
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5_000_000.01)]    // just above maximum
    [InlineData(10_000_000)]
    public void Create_WithOutOfRangePremiumAmount_ShouldThrowDomainException(decimal amount)
    {
        // Act
        var act = () => CreateValidPolicy(premiumAmount: amount);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*1,000*5,000,000*");
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000_000)]
    [InlineData(2_500_000)]
    public void Create_WithPremiumAtBoundary_ShouldNotThrow(decimal amount)
    {
        // Act
        var act = () => CreateValidPolicy(premiumAmount: amount);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("usd")]   // wrong case
    [InlineData("")]
    public void Create_WithInvalidCurrency_ShouldThrowDomainException(string currency)
    {
        // Act
        var act = () => CreateValidPolicy(currency: currency);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WhenEffectiveDateEqualsExpiryDate_ShouldThrowDomainException()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 1);

        // Act
        var act = () => CreateValidPolicy(effectiveDate: date, expiryDate: date);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*Effective date*expiry date*");
    }

    [Fact]
    public void Create_WhenEffectiveDateAfterExpiryDate_ShouldThrowDomainException()
    {
        // Arrange
        var effective = new DateOnly(2026, 1, 1);
        var expiry = new DateOnly(2025, 1, 1);

        // Act
        var act = () => CreateValidPolicy(effectiveDate: effective, expiryDate: expiry);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Europe")]
    [InlineData("USA")]
    public void Create_WithInvalidRegion_ShouldThrowDomainException(string region)
    {
        // Act
        var act = () => CreateValidPolicy(region: region);

        // Assert
        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------
    // Policy.FlagForReview — first call
    // ------------------------------------------------------------------

    [Fact]
    public void FlagForReview_WhenNotYetFlagged_ShouldSetFlaggedForReviewToTrue()
    {
        // Arrange
        var policy = CreateValidPolicy();
        policy.FlaggedForReview.Should().BeFalse();

        // Act
        policy.FlagForReview();

        // Assert
        policy.FlaggedForReview.Should().BeTrue();
    }

    [Fact]
    public void FlagForReview_WhenNotYetFlagged_ShouldRaiseExactlyOneDomainEvent()
    {
        // Arrange
        var policy = CreateValidPolicy();

        // Act
        policy.FlagForReview();

        // Assert
        policy.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PolicyFlaggedForReviewEvent>();
    }

    [Fact]
    public void FlagForReview_WhenNotYetFlagged_EventShouldContainCorrectPolicyId()
    {
        // Arrange
        var policy = CreateValidPolicy();

        // Act
        policy.FlagForReview();

        // Assert
        var evt = policy.DomainEvents.Single().Should().BeOfType<PolicyFlaggedForReviewEvent>().Subject;
        evt.PolicyId.Should().Be(policy.Id);
        evt.FlaggedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ------------------------------------------------------------------
    // Policy.FlagForReview — idempotent second call
    // ------------------------------------------------------------------

    [Fact]
    public void FlagForReview_WhenAlreadyFlagged_ShouldRemainFlaggedForReviewTrue()
    {
        // Arrange
        var policy = CreateValidPolicy();
        policy.FlagForReview();
        policy.ClearDomainEvents();

        // Act
        policy.FlagForReview();

        // Assert
        policy.FlaggedForReview.Should().BeTrue();
    }

    [Fact]
    public void FlagForReview_WhenAlreadyFlagged_ShouldNotRaiseAdditionalEvent()
    {
        // Arrange
        var policy = CreateValidPolicy();
        policy.FlagForReview();
        policy.ClearDomainEvents();

        // Act
        policy.FlagForReview();

        // Assert
        policy.DomainEvents.Should().BeEmpty();
    }
}
