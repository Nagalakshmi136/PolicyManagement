using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;

namespace PolicyManagement.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent data seeder. Seeds 200+ policy records from
/// <c>Persistence/Seed/Data/policies.json</c> when the table is empty.
/// Invoked at application startup in Development and Integration environments.
/// </summary>
public static class DataSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task SeedAsync(AppDbContext db, ILogger logger)
    {
        await SeedPoliciesAsync(db, logger);
    }

    private static async Task SeedPoliciesAsync(AppDbContext db, ILogger logger)
    {
        if (await db.Policies.AnyAsync())
        {
            logger.LogInformation("Policies table already has data — skipping seed.");
            return;
        }

        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Persistence", "Seed", "Data", "policies.json");

        if (!File.Exists(jsonPath))
        {
            logger.LogWarning("Seed file not found at {Path} — skipping seed.", jsonPath);
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var dtos = JsonSerializer.Deserialize<List<PolicySeedDto>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialise policies seed data.");

        var policies = new List<Policy>(dtos.Count);

        foreach (var dto in dtos)
        {
            var lob = dto.LineOfBusiness switch
            {
                // "A&H" is the database/wire representation of AccidentAndHealth.
                // "AccidentAndHealth" is also accepted here (the enum name) because
                // some seed files may use the C# name rather than the stored value.
                // Both resolve to LineOfBusiness.AccidentAndHealth.
                "A&H"               => LineOfBusiness.AccidentAndHealth,
                "AccidentAndHealth" => LineOfBusiness.AccidentAndHealth,
                _                   => Enum.Parse<LineOfBusiness>(dto.LineOfBusiness)
            };

            var status       = Enum.Parse<PolicyStatus>(dto.Status);
            var effectiveDate = DateOnly.Parse(dto.EffectiveDate);
            var expiryDate    = DateOnly.Parse(dto.ExpiryDate);

            var policy = Policy.Create(
                policyNumber:      dto.PolicyNumber,
                policyholderName:  dto.PolicyholderName,
                lineOfBusiness:    lob,
                status:            status,
                premiumAmount:     dto.PremiumAmount,
                currency:          dto.Currency,
                effectiveDate:     effectiveDate,
                expiryDate:        expiryDate,
                region:            dto.Region,
                underwriter:       dto.Underwriter);

            if (dto.FlaggedForReview)
                policy.FlagForReview();

            policies.Add(policy);
        }

        await db.Policies.AddRangeAsync(policies);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} policies.", policies.Count);
    }

    // ------------------------------------------------------------------
    // Internal DTO — only used during seeding
    // ------------------------------------------------------------------

    private sealed class PolicySeedDto
    {
        [JsonPropertyName("policyNumber")]
        public string PolicyNumber { get; init; } = string.Empty;

        [JsonPropertyName("policyholderName")]
        public string PolicyholderName { get; init; } = string.Empty;

        [JsonPropertyName("lineOfBusiness")]
        public string LineOfBusiness { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("premiumAmount")]
        public decimal PremiumAmount { get; init; }

        [JsonPropertyName("currency")]
        public string Currency { get; init; } = string.Empty;

        [JsonPropertyName("effectiveDate")]
        public string EffectiveDate { get; init; } = string.Empty;

        [JsonPropertyName("expiryDate")]
        public string ExpiryDate { get; init; } = string.Empty;

        [JsonPropertyName("region")]
        public string Region { get; init; } = string.Empty;

        [JsonPropertyName("underwriter")]
        public string Underwriter { get; init; } = string.Empty;

        [JsonPropertyName("flaggedForReview")]
        public bool FlaggedForReview { get; init; }
    }
}
