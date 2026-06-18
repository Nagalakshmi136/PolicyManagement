using FluentAssertions;
using PolicyManagement.Application.Policies.Commands.FlagPolicies;

namespace PolicyManagement.Application.Tests.Policies.Commands;

public sealed class FlagPoliciesCommandValidatorTests
{
    private readonly FlagPoliciesCommandValidator _sut = new();

    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_SingleValidUuid_PassesValidation()
    {
        var result = _sut.Validate(new FlagPoliciesCommand([Guid.NewGuid().ToString()]));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_HundredValidUuids_PassesValidation()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid().ToString()).ToList();

        var result = _sut.Validate(new FlagPoliciesCommand(ids));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Empty array fails — per ADR-007 and OpenAPI minItems: 1
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyPolicyIds_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new FlagPoliciesCommand([]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PolicyIds");
    }

    // ------------------------------------------------------------------
    // More than 100 IDs fails — per OpenAPI maxItems: 100
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_OneHundredAndOneIds_ReturnsValidationFailure()
    {
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid().ToString()).ToList();

        var result = _sut.Validate(new FlagPoliciesCommand(ids));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "PolicyIds" &&
            e.ErrorMessage.Contains("100"));
    }

    [Fact]
    public void Validate_TwoHundredIds_ReturnsValidationFailure()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid().ToString()).ToList();

        var result = _sut.Validate(new FlagPoliciesCommand(ids));

        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Non-UUID IDs fail element-level validation
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("POL-000001")]
    [InlineData("12345")]
    [InlineData("3fa85f64-5717-4562-b3fc-ZZZZZZZZZZZZ")]
    public void Validate_NonUuidElement_ReturnsValidationFailure(string invalidId)
    {
        var ids = new List<string> { Guid.NewGuid().ToString(), invalidId };

        var result = _sut.Validate(new FlagPoliciesCommand(ids));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("valid UUID"));
    }

    // ------------------------------------------------------------------
    // Empty string element fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyStringElement_ReturnsValidationFailure()
    {
        var result = _sut.Validate(new FlagPoliciesCommand([string.Empty]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("empty") || e.ErrorMessage.Contains("UUID"));
    }

    // ------------------------------------------------------------------
    // Exactly 100 IDs is the boundary — must pass
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_ExactlyOneHundredIds_PassesValidation()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid().ToString()).ToList();

        var result = _sut.Validate(new FlagPoliciesCommand(ids));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Exactly 1 ID is the minimum boundary — must pass
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_ExactlyOneId_PassesValidation()
    {
        var result = _sut.Validate(new FlagPoliciesCommand([Guid.NewGuid().ToString()]));

        result.IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Mixed valid and invalid — overall fails
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_MixOfValidAndInvalidIds_ReturnsValidationFailure()
    {
        var ids = new List<string>
        {
            Guid.NewGuid().ToString(),
            "not-valid",
            Guid.NewGuid().ToString(),
        };

        var result = _sut.Validate(new FlagPoliciesCommand(ids));

        result.IsValid.Should().BeFalse();
    }
}
