using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class KycReviewSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxFingerprint",
                schema: "kyc",
                table: "identity_snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_snapshots_TaxFingerprint",
                schema: "kyc",
                table: "identity_snapshots",
                column: "TaxFingerprint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_identity_snapshots_TaxFingerprint",
                schema: "kyc",
                table: "identity_snapshots");

            migrationBuilder.DropColumn(
                name: "TaxFingerprint",
                schema: "kyc",
                table: "identity_snapshots");
        }
    }
}
