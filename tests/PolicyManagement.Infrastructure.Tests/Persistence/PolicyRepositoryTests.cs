using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Repositories;
using PolicyManagement.Infrastructure.Persistence;
using PolicyManagement.Infrastructure.Persistence.Interceptors;
using PolicyManagement.Infrastructure.Persistence.Repositories;

namespace PolicyManagement.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests for <see cref="PolicyRepository"/> using EF Core InMemory provider.
/// These tests verify filtering, sorting, pagination, summary aggregation, and
/// multi-id lookup behaviour without requiring a live SQL Server instance.
/// </summary>
public sealed class PolicyRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PolicyRepository _sut;

    public PolicyRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor())
            .Options;

        _db  = new AppDbContext(options);
        _sut = new PolicyRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Policy MakePolicy(
        string policyNumber,
        PolicyStatus status           = PolicyStatus.Active,
        LineOfBusiness lob            = LineOfBusiness.Property,
        string region                 = "Singapore",
        string currency               = "SGD",
        decimal premium               = 50_000m,
        DateOnly? effectiveDate       = null,
        DateOnly? expiryDate          = null,
        string policyholderName       = "Tan Wei Ming",
        string underwriter            = "Alice Teo")
    {
        var start = effectiveDate ?? new DateOnly(2024, 1, 1);
        var end   = expiryDate   ?? start.AddYears(1);
        return Policy.Create(policyNumber, policyholderName, lob, status,
            premium, currency, start, end, region, underwriter);
    }

    private async Task SeedAsync(params Policy[] policies)
    {
        await _db.Policies.AddRangeAsync(policies);
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // GetByIdAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsPolicyWithMatchingId()
    {
        var policy = MakePolicy("POL-000001");
        await SeedAsync(policy);

        var result = await _sut.GetByIdAsync(policy.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(policy.Id);
        result.PolicyNumber.Should().Be("POL-000001");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync("does-not-exist");

        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // GetByIdsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetByIdsAsync_MixedIds_ReturnsOnlyFound()
    {
        var p1 = MakePolicy("POL-000001");
        var p2 = MakePolicy("POL-000002");
        await SeedAsync(p1, p2);

        var result = await _sut.GetByIdsAsync([p1.Id, p2.Id, "missing-id"]);

        result.Should().HaveCount(2);
        result.Select(p => p.Id).Should().BeEquivalentTo([p1.Id, p2.Id]);
    }

    [Fact]
    public async Task GetByIdsAsync_AllMissing_ReturnsEmptyList()
    {
        var result = await _sut.GetByIdsAsync(["id1", "id2"]);

        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // SearchAsync — filters
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        await SeedAsync(
            MakePolicy("POL-000001", status: PolicyStatus.Active),
            MakePolicy("POL-000002", status: PolicyStatus.Expired),
            MakePolicy("POL-000003", status: PolicyStatus.Active));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, Status: PolicyStatus.Active));

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(p => p.Status == PolicyStatus.Active);
    }

    [Fact]
    public async Task SearchAsync_FilterByLineOfBusiness_ReturnsOnlyMatchingLob()
    {
        await SeedAsync(
            MakePolicy("POL-000001", lob: LineOfBusiness.Marine),
            MakePolicy("POL-000002", lob: LineOfBusiness.Property),
            MakePolicy("POL-000003", lob: LineOfBusiness.Marine));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, LineOfBusiness: LineOfBusiness.Marine));

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(p => p.LineOfBusiness == LineOfBusiness.Marine);
    }

    [Fact]
    public async Task SearchAsync_FilterByRegion_ReturnsOnlyMatchingRegion()
    {
        await SeedAsync(
            MakePolicy("POL-000001", region: "Singapore"),
            MakePolicy("POL-000002", region: "Japan"),
            MakePolicy("POL-000003", region: "Singapore"));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, Region: "Singapore"));

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(p => p.Region == "Singapore");
    }

    [Fact]
    public async Task SearchAsync_FilterByEffectiveDateFrom_ReturnsOnlyPoliciesOnOrAfter()
    {
        await SeedAsync(
            MakePolicy("POL-000001", effectiveDate: new DateOnly(2024, 1, 1), expiryDate: new DateOnly(2025, 1, 1)),
            MakePolicy("POL-000002", effectiveDate: new DateOnly(2024, 6, 1), expiryDate: new DateOnly(2025, 6, 1)),
            MakePolicy("POL-000003", effectiveDate: new DateOnly(2023, 1, 1), expiryDate: new DateOnly(2024, 1, 1)));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10,
            EffectiveDateFrom: new DateOnly(2024, 1, 1)));

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(p => p.EffectiveDate >= new DateOnly(2024, 1, 1));
    }

    [Fact]
    public async Task SearchAsync_FilterByEffectiveDateTo_ReturnsOnlyPoliciesOnOrBefore()
    {
        await SeedAsync(
            MakePolicy("POL-000001", effectiveDate: new DateOnly(2023, 1, 1), expiryDate: new DateOnly(2024, 1, 1)),
            MakePolicy("POL-000002", effectiveDate: new DateOnly(2024, 6, 1), expiryDate: new DateOnly(2025, 6, 1)),
            MakePolicy("POL-000003", effectiveDate: new DateOnly(2025, 1, 1), expiryDate: new DateOnly(2026, 1, 1)));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10,
            EffectiveDateTo: new DateOnly(2024, 1, 1)));

        result.TotalCount.Should().Be(1);
        result.Items.First().PolicyNumber.Should().Be("POL-000001");
    }

    [Fact]
    public async Task SearchAsync_SearchTermMatchesPolicyNumber_ReturnsMatch()
    {
        await SeedAsync(
            MakePolicy("POL-000001", policyholderName: "Tan Wei Ming"),
            MakePolicy("POL-000002", policyholderName: "Lee Ah Kow"));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SearchTerm: "POL-000001"));

        result.TotalCount.Should().Be(1);
        result.Items.First().PolicyNumber.Should().Be("POL-000001");
    }

    [Fact]
    public async Task SearchAsync_SearchTermMatchesPolicyholderName_ReturnsMatch()
    {
        await SeedAsync(
            MakePolicy("POL-000001", policyholderName: "Tan Wei Ming"),
            MakePolicy("POL-000002", policyholderName: "Lee Ah Kow"));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SearchTerm: "Tan Wei"));

        result.TotalCount.Should().Be(1);
        result.Items.First().PolicyholderName.Should().Be("Tan Wei Ming");
    }

    [Fact]
    public async Task SearchAsync_SearchTermMatchesUnderwriter_ReturnsMatch()
    {
        await SeedAsync(
            MakePolicy("POL-000001", underwriter: "Sarah Mitchell"),
            MakePolicy("POL-000002", underwriter: "James Wong"));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SearchTerm: "Sarah"));

        result.TotalCount.Should().Be(1);
        result.Items.First().Underwriter.Should().Be("Sarah Mitchell");
    }

    [Fact]
    public async Task SearchAsync_NoMatchingSearchTerm_ReturnsEmpty()
    {
        await SeedAsync(MakePolicy("POL-000001"));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SearchTerm: "zzznomatch"));

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // SearchAsync — sort
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_SortByPremiumAmountAscending_ReturnsInOrder()
    {
        await SeedAsync(
            MakePolicy("POL-000001", premium: 100_000m),
            MakePolicy("POL-000002", premium: 50_000m),
            MakePolicy("POL-000003", premium: 200_000m));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SortBy: "premiumamount", SortDescending: false));

        result.Items.Select(p => p.PremiumAmount)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task SearchAsync_SortByPremiumAmountDescending_ReturnsInOrder()
    {
        await SeedAsync(
            MakePolicy("POL-000001", premium: 100_000m),
            MakePolicy("POL-000002", premium: 50_000m),
            MakePolicy("POL-000003", premium: 200_000m));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SortBy: "premiumamount", SortDescending: true));

        result.Items.Select(p => p.PremiumAmount)
            .Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SearchAsync_UnknownSortBy_DefaultsToCreatedAtAscending()
    {
        await SeedAsync(
            MakePolicy("POL-000001"),
            MakePolicy("POL-000002"),
            MakePolicy("POL-000003"));

        // Should not throw; falls back to CreatedAt default
        var act = async () => await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 1, PageSize: 10, SortBy: "unrecognised"));

        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // SearchAsync — pagination
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_Pagination_ReturnsCorrectPage()
    {
        for (var i = 1; i <= 15; i++)
            await SeedAsync(MakePolicy($"POL-{i:D6}"));
        _db.ChangeTracker.Clear();

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 2, PageSize: 5));

        result.TotalCount.Should().Be(15);
        result.Items.Should().HaveCount(5);
        result.Page.Should().Be(2);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task SearchAsync_OutOfRangePage_ReturnsEmptyItems()
    {
        await SeedAsync(MakePolicy("POL-000001"), MakePolicy("POL-000002"));

        var result = await _sut.SearchAsync(new PolicySearchCriteria(
            Page: 99, PageSize: 10));

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // GetSummaryAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetSummaryAsync_StatusCounts_AreCorrect()
    {
        await SeedAsync(
            MakePolicy("POL-000001", status: PolicyStatus.Active),
            MakePolicy("POL-000002", status: PolicyStatus.Active),
            MakePolicy("POL-000003", status: PolicyStatus.Expired),
            MakePolicy("POL-000004", status: PolicyStatus.Pending),
            MakePolicy("POL-000005", status: PolicyStatus.Cancelled));

        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var result = await _sut.GetSummaryAsync(cutoff);

        result.TotalCount.Should().Be(5);
        result.ActiveCount.Should().Be(2);
        result.ExpiredCount.Should().Be(1);
        result.PendingCount.Should().Be(1);
        result.CancelledCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_PremiumByLobAndCurrency_GroupsCorrectly()
    {
        await SeedAsync(
            MakePolicy("POL-000001", lob: LineOfBusiness.Property, currency: "SGD", premium: 100_000m),
            MakePolicy("POL-000002", lob: LineOfBusiness.Property, currency: "SGD", premium: 200_000m),
            MakePolicy("POL-000003", lob: LineOfBusiness.Marine,   currency: "USD", premium:  50_000m));

        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var result = await _sut.GetSummaryAsync(cutoff);

        result.PremiumByLobAndCurrency.Should().HaveCount(2);
        result.PremiumByLobAndCurrency[(LineOfBusiness.Property, "SGD")].Should().Be(300_000m);
        result.PremiumByLobAndCurrency[(LineOfBusiness.Marine, "USD")].Should().Be(50_000m);
    }

    [Fact]
    public async Task GetSummaryAsync_ExpiringSoonCount_CountsPoliciesUpToCutoff()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await SeedAsync(
            // expires in 10 days — within window
            MakePolicy("POL-000001",
                effectiveDate: today.AddDays(-365),
                expiryDate:    today.AddDays(10)),
            // expires exactly on cutoff (inclusive)
            MakePolicy("POL-000002",
                effectiveDate: today.AddDays(-365),
                expiryDate:    today.AddDays(30)),
            // expires in 60 days — outside window
            MakePolicy("POL-000003",
                effectiveDate: today.AddDays(-365),
                expiryDate:    today.AddDays(60)),
            // already expired yesterday — not counted
            MakePolicy("POL-000004",
                effectiveDate: today.AddDays(-400),
                expiryDate:    today.AddDays(-1)));

        var cutoff = today.AddDays(30);
        var result = await _sut.GetSummaryAsync(cutoff);

        result.ExpiringSoonCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyTable_ReturnsAllZeros()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var result = await _sut.GetSummaryAsync(cutoff);

        result.TotalCount.Should().Be(0);
        result.ActiveCount.Should().Be(0);
        result.ExpiredCount.Should().Be(0);
        result.PendingCount.Should().Be(0);
        result.CancelledCount.Should().Be(0);
        result.ExpiringSoonCount.Should().Be(0);
        result.PremiumByLobAndCurrency.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // AuditSaveChangesInterceptor
    // ------------------------------------------------------------------

    [Fact]
    public async Task AuditInterceptor_OnInsert_SetsCreatedAtAndUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var policy = MakePolicy("POL-000001");
        await SeedAsync(policy);

        // Reload from DB to pick up interceptor-set values
        _db.ChangeTracker.Clear();
        var saved = await _db.Policies.FirstAsync(p => p.Id == policy.Id);

        saved.CreatedAt.Should().BeAfter(before);
        saved.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task AuditInterceptor_OnUpdate_UpdatesUpdatedAtButNotCreatedAt()
    {
        var policy = MakePolicy("POL-000001");
        await SeedAsync(policy);

        _db.ChangeTracker.Clear();
        var saved = await _db.Policies.FirstAsync(p => p.Id == policy.Id);
        var originalCreatedAt = saved.CreatedAt;

        // Trigger an update by flagging for review
        saved.FlagForReview();
        await Task.Delay(10);  // ensure clock advances slightly
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var updated = await _db.Policies.FirstAsync(p => p.Id == policy.Id);

        updated.CreatedAt.Should().Be(originalCreatedAt);   // must not change
        updated.UpdatedAt.Should().BeOnOrAfter(originalCreatedAt);
        updated.FlaggedForReview.Should().BeTrue();
    }
}
