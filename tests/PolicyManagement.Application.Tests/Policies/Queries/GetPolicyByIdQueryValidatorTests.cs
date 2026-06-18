using FluentAssertions;
using PolicyManagement.Application.Policies.Queries.GetPolicyById;

namespace PolicyManagement.Application.Tests.Policies.Queries;

public sealed class GetPolicyByIdQueryValidatorTests
{
    private readonly GetPolicyByIdQueryValidator _sut = new();

    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_ValidUuid_PassesValidation()
    {
        var result = _sut.Validate(new GetPolicyByIdQuery(Guid.NewGuid().ToString()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UppercaseUuid_PassesValidation()
    {
        var result = _sut.Validate(new GetPolicyByIdQuery(Guid.NewGuid().ToString().ToUpperInvariant()));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Empty / null id fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyString_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new GetPolicyByIdQuery(string.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Id");
    }

    [Fact]
    public void Validate_WhitespaceString_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new GetPolicyByIdQuery("   "));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Id");
    }

    // ------------------------------------------------------------------
    // Non-UUID string fails
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("POL-000001")]
    [InlineData("12345")]
    [InlineData("3fa85f64-5717-4562-b3fc-ZZZZZZZZZZZZ")]
    public void Validate_NonUuidString_ReturnsValidationFailure(string id)
    {
        var result = _sut.Validate(new GetPolicyByIdQuery(id));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Id" &&
            e.ErrorMessage.Contains("valid UUID"));
    }
}
