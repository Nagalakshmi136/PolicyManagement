using System.Text.RegularExpressions;
using PolicyManagement.Domain.Common;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Events;
using PolicyManagement.Domain.Exceptions;

namespace PolicyManagement.Domain.Entities;

/// <summary>
/// The sole aggregate root of the Policy Management domain.
/// All business invariants are enforced through domain methods;
/// direct property mutation from outside this class is not permitted.
/// </summary>
public sealed class Policy : AggregateRoot
{
    // ------------------------------------------------------------------
    // Properties
    // ------------------------------------------------------------------

    /// <summary>Surrogate UUID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Unique business key in POL-XXXXXX format.</summary>
    public string PolicyNumber { get; private set; } = string.Empty;

    /// <summary>Name of the insured party.</summary>
    public string PolicyholderName { get; private set; } = string.Empty;

    /// <summary>Line of business for this policy.</summary>
    public LineOfBusiness LineOfBusiness { get; private set; }

    /// <summary>Current lifecycle status of the policy.</summary>
    public PolicyStatus Status { get; private set; }

    /// <summary>
    /// Policy premium. Range: 1,000 – 5,000,000. Stored as decimal(18,2).
    /// Never use float or double for monetary values.
    /// </summary>
    public decimal PremiumAmount { get; private set; }

    /// <summary>ISO 4217 currency code. One of: USD, SGD, HKD, AUD, JPY, THB.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Coverage start date.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>Coverage end date.</summary>
    public DateOnly ExpiryDate { get; private set; }

    /// <summary>
    /// APAC region. One of: Singapore, Hong Kong, Australia, Japan,
    /// Thailand, Indonesia, Malaysia, Philippines.
    /// </summary>
    public string Region { get; private set; } = string.Empty;

    /// <summary>Assigned underwriter name.</summary>
    public string Underwriter { get; private set; } = string.Empty;

    /// <summary>
    /// True when the policy has been flagged for underwriter review.
    /// Set exclusively via <see cref="FlagForReview"/>; default is false.
    /// </summary>
    public bool FlaggedForReview { get; private set; }

    /// <summary>UTC timestamp of record creation. Set by AuditSaveChangesInterceptor.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC timestamp of last update. Set by AuditSaveChangesInterceptor.</summary>
    public DateTime UpdatedAt { get; private set; }

    // ------------------------------------------------------------------
    // EF Core requires a parameterless constructor (private is sufficient).
    // ------------------------------------------------------------------
#pragma warning disable CS8618
    private Policy() { }
#pragma warning restore CS8618

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    private static readonly Regex PolicyNumberPattern =
        new(@"^POL-\d{6}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private static readonly IReadOnlySet<string> ValidCurrencies =
        new HashSet<string>(StringComparer.Ordinal) { "USD", "SGD", "HKD", "AUD", "JPY", "THB" };

    private static readonly IReadOnlySet<string> ValidRegions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Singapore", "Hong Kong", "Australia", "Japan",
            "Thailand", "Indonesia", "Malaysia", "Philippines"
        };

    /// <summary>
    /// Creates a new <see cref="Policy"/> aggregate, enforcing all domain
    /// invariants. Throws <see cref="DomainException"/> on violation.
    /// </summary>
    public static Policy Create(
        string policyNumber,
        string policyholderName,
        LineOfBusiness lineOfBusiness,
        PolicyStatus status,
        decimal premiumAmount,
        string currency,
        DateOnly effectiveDate,
        DateOnly expiryDate,
        string region,
        string underwriter)
    {
        if (string.IsNullOrWhiteSpace(policyNumber) || !PolicyNumberPattern.IsMatch(policyNumber))
            throw new DomainException("Policy number must match the format POL-XXXXXX (six digits).");

        if (string.IsNullOrWhiteSpace(policyholderName))
            throw new DomainException("Policyholder name must not be empty.");

        if (premiumAmount < 1_000m || premiumAmount > 5_000_000m)
            throw new DomainException("Premium amount must be between 1,000 and 5,000,000.");

        if (string.IsNullOrWhiteSpace(currency) || !ValidCurrencies.Contains(currency))
            throw new DomainException($"Currency must be one of: {string.Join(", ", ValidCurrencies)}.");

        if (effectiveDate >= expiryDate)
            throw new DomainException("Effective date must be earlier than expiry date.");

        if (string.IsNullOrWhiteSpace(region) || !ValidRegions.Contains(region))
            throw new DomainException($"Region must be one of the eight supported APAC values.");

        if (string.IsNullOrWhiteSpace(underwriter))
            throw new DomainException("Underwriter must not be empty.");

        return new Policy
        {
            Id = Guid.NewGuid().ToString(),
            PolicyNumber = policyNumber,
            PolicyholderName = policyholderName,
            LineOfBusiness = lineOfBusiness,
            Status = status,
            PremiumAmount = premiumAmount,
            Currency = currency,
            EffectiveDate = effectiveDate,
            ExpiryDate = expiryDate,
            Region = region,
            Underwriter = underwriter,
            FlaggedForReview = false
        };
    }

    // ------------------------------------------------------------------
    // Domain behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Marks the policy for underwriter review.
    /// Idempotent: calling on an already-flagged policy is a no-op.
    /// Raises <see cref="PolicyFlaggedForReviewEvent"/> on first flag.
    /// </summary>
    public void FlagForReview()
    {
        if (FlaggedForReview)
            return;

        FlaggedForReview = true;
        RaiseDomainEvent(new PolicyFlaggedForReviewEvent(Id, DateTime.UtcNow));
    }
}
