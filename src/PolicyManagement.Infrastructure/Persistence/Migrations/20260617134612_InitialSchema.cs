using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    PolicyId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PolicyNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PolicyholderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LineOfBusiness = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PremiumAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Underwriter = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FlaggedForReview = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.PolicyId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_CreatedAt",
                table: "Policies",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_EffectiveDate",
                table: "Policies",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_ExpiryDate",
                table: "Policies",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_FlaggedForReview",
                table: "Policies",
                column: "FlaggedForReview");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_LineOfBusiness",
                table: "Policies",
                column: "LineOfBusiness");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_PolicyNumber_U",
                table: "Policies",
                column: "PolicyNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Region",
                table: "Policies",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Status",
                table: "Policies",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Policies");
        }
    }
}
