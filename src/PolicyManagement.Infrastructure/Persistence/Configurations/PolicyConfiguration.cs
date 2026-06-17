using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;

namespace PolicyManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Policy"/> aggregate.
/// No Data Annotations are placed on domain entities — all configuration lives here.
/// </summary>
internal sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("Policies");

        // ------------------------------------------------------------------
        // Primary key
        // ------------------------------------------------------------------
        builder.HasKey(p => p.Id)
            .HasName("PK_Policies");

        builder.Property(p => p.Id)
            .HasColumnName("PolicyId")
            .HasMaxLength(50)
            .IsRequired();

        // ------------------------------------------------------------------
        // Business key
        // ------------------------------------------------------------------
        builder.Property(p => p.PolicyNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(p => p.PolicyNumber)
            .IsUnique()
            .HasDatabaseName("IX_Policies_PolicyNumber_U");

        // ------------------------------------------------------------------
        // Policyholder / underwriter
        // ------------------------------------------------------------------
        builder.Property(p => p.PolicyholderName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Underwriter)
            .HasMaxLength(200)
            .IsRequired();

        // ------------------------------------------------------------------
        // Enums — stored as nvarchar strings, not integers.
        // LineOfBusiness.AccidentAndHealth ↔ "A&H" in the database.
        // ------------------------------------------------------------------
        var lobConverter = new ValueConverter<LineOfBusiness, string>(
            v => v == LineOfBusiness.AccidentAndHealth ? "A&H" : v.ToString(),
            v => v == "A&H" ? LineOfBusiness.AccidentAndHealth
                            : Enum.Parse<LineOfBusiness>(v));

        builder.Property(p => p.LineOfBusiness)
            .HasConversion(lobConverter)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ------------------------------------------------------------------
        // Monetary / date fields
        // ------------------------------------------------------------------
        builder.Property(p => p.PremiumAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(p => p.EffectiveDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.ExpiryDate)
            .HasColumnType("date")
            .IsRequired();

        // ------------------------------------------------------------------
        // Region / flags / audit
        // ------------------------------------------------------------------
        builder.Property(p => p.Region)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.FlaggedForReview)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        // ------------------------------------------------------------------
        // Indexes — all filterable / sortable columns have named indexes.
        // ------------------------------------------------------------------
        builder.HasIndex(p => p.Status)
            .HasDatabaseName("IX_Policies_Status");

        builder.HasIndex(p => p.LineOfBusiness)
            .HasDatabaseName("IX_Policies_LineOfBusiness");

        builder.HasIndex(p => p.Region)
            .HasDatabaseName("IX_Policies_Region");

        builder.HasIndex(p => p.EffectiveDate)
            .HasDatabaseName("IX_Policies_EffectiveDate");

        builder.HasIndex(p => p.ExpiryDate)
            .HasDatabaseName("IX_Policies_ExpiryDate");

        builder.HasIndex(p => p.FlaggedForReview)
            .HasDatabaseName("IX_Policies_FlaggedForReview");

        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("IX_Policies_CreatedAt");
    }
}
