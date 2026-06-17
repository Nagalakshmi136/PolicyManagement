using FluentAssertions;
using FluentValidation;
using PolicyManagement.Application.Policies.Queries.ListPolicies;

namespace PolicyManagement.Application.Tests.Policies.Queries;

public sealed class ListPoliciesQueryValidatorTests
{
    private readonly ListPoliciesQueryValidator _sut = new();

    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_DefaultQuery_PassesValidation()
    {
        var result = _sut.Validate(new ListPoliciesQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllValidParameters_PassesValidation()
    {
        var query = new ListPoliciesQuery(
            Page: 2,
            PageSize: 50,
            Status: "Active",
            LineOfBusiness: "Marine",
            Region: "Japan",
            EffectiveDateFrom: new DateOnly(2024, 1, 1),
            EffectiveDateTo: new DateOnly(2024, 12, 31),
            Search: "Tanaka",
            SortBy: "premiumAmount",
            SortDirection: "desc");

        var result = _sut.Validate(query);

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // PageSize > 100 fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_PageSizeAbove100_ReturnsValidationFailure()
    {
        var query = new ListPoliciesQuery(PageSize: 101);

        var result = _sut.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "PageSize" &&
            e.ErrorMessage.Contains("must not exceed 100"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageSizeLessThan1_ReturnsValidationFailure(int pageSize)
    {
        var result = _sut.Validate(new ListPoliciesQuery(PageSize: pageSize));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    // ------------------------------------------------------------------
    // Invalid status enum fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_InvalidStatusEnum_ReturnsValidationFailure()
    {
        var query = new ListPoliciesQuery(Status: "Suspended");

        var result = _sut.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "Status" &&
            e.ErrorMessage.Contains("Active, Expired, Pending, Cancelled"));
    }

    [Theory]
    [InlineData("active")]   // wrong case
    [InlineData("ACTIVE")]
    [InlineData("unknown")]
    public void Validate_StatusCaseMismatch_ReturnsValidationFailure(string status)
    {
        var result = _sut.Validate(new ListPoliciesQuery(Status: status));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Expired")]
    [InlineData("Pending")]
    [InlineData("Cancelled")]
    public void Validate_ValidStatus_PassesValidation(string status)
    {
        var result = _sut.Validate(new ListPoliciesQuery(Status: status));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // EffectiveDateFrom after EffectiveDateTo fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_EffectiveDateFromAfterEffectiveDateTo_ReturnsValidationFailure()
    {
        var query = new ListPoliciesQuery(
            EffectiveDateFrom: new DateOnly(2024, 12, 31),
            EffectiveDateTo: new DateOnly(2024, 1, 1));

        var result = _sut.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "effectiveDateFrom" &&
            e.ErrorMessage.Contains("effectiveDateFrom must not be after effectiveDateTo"));
    }

    [Fact]
    public void Validate_EffectiveDateFromEqualsEffectiveDateTo_PassesValidation()
    {
        var date = new DateOnly(2024, 6, 1);
        var query = new ListPoliciesQuery(EffectiveDateFrom: date, EffectiveDateTo: date);

        var result = _sut.Validate(query);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_OnlyEffectiveDateFromProvided_PassesValidation()
    {
        var result = _sut.Validate(new ListPoliciesQuery(EffectiveDateFrom: new DateOnly(2024, 1, 1)));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Invalid sortBy field fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_InvalidSortByField_ReturnsValidationFailure()
    {
        var query = new ListPoliciesQuery(SortBy: "nonExistentField");

        var result = _sut.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "SortBy" &&
            e.ErrorMessage.Contains("allowed field names"));
    }

    [Theory]
    [InlineData("policyNumber")]
    [InlineData("policyholderName")]
    [InlineData("lineOfBusiness")]
    [InlineData("status")]
    [InlineData("premiumAmount")]
    [InlineData("currency")]
    [InlineData("effectiveDate")]
    [InlineData("expiryDate")]
    [InlineData("region")]
    [InlineData("underwriter")]
    [InlineData("flaggedForReview")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    public void Validate_AllAllowedSortByFields_PassValidation(string sortBy)
    {
        var result = _sut.Validate(new ListPoliciesQuery(SortBy: sortBy));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Invalid sortDirection fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_InvalidSortDirection_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new ListPoliciesQuery(SortDirection: "ascending"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "SortDirection");
    }

    // ------------------------------------------------------------------
    // Invalid lineOfBusiness fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_InvalidLineOfBusiness_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new ListPoliciesQuery(LineOfBusiness: "Life"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "LineOfBusiness" &&
            e.ErrorMessage.Contains("Property, Casualty, A&H, Marine"));
    }

    [Theory]
    [InlineData("Property")]
    [InlineData("Casualty")]
    [InlineData("A&H")]
    [InlineData("Marine")]
    public void Validate_ValidLineOfBusiness_PassesValidation(string lob)
    {
        var result = _sut.Validate(new ListPoliciesQuery(LineOfBusiness: lob));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Page < 1 fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_PageLessThan1_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new ListPoliciesQuery(Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }
}
