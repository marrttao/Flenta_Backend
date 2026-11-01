using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class DiditSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET CONSTRAINTS ALL IMMEDIATE; SET CONSTRAINTS ALL DEFERRED;");
            migrationBuilder.DropIndex(
                name: "IX_verifications_ApplicantId",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropIndex(
                name: "IX_verifications_ExternalUserId",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropIndex(
                name: "IX_verifications_OwnerId",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_kyc_status",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.RenameColumn(
                name: "ReviewId",
                schema: "kyc",
                table: "verifications",
                newName: "DecisionReference");

            migrationBuilder.RenameColumn(
                name: "LevelName",
                schema: "kyc",
                table: "verifications",
                newName: "ConfigurationId");

            migrationBuilder.RenameColumn(
                name: "ApplicantId",
                schema: "kyc",
                table: "verifications",
                newName: "ProviderSessionId");

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                schema: "kyc",
                table: "verifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                schema: "kyc",
                table: "verifications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Sumsub");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_ExternalUserId",
                schema: "kyc",
                table: "verifications",
                column: "ExternalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_OwnerId_ProviderName_AttemptNumber",
                schema: "kyc",
                table: "verifications",
                columns: new[] { "OwnerId", "ProviderName", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verifications_ProviderName_ProviderSessionId",
                schema: "kyc",
                table: "verifications",
                columns: new[] { "ProviderName", "ProviderSessionId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_kyc_status",
                schema: "kyc",
                table: "verifications",
                sql: "\"Status\" IN ('NotStarted','InProgress','PendingReview','NeedsResubmission','Rejected','Verified','Expired')");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION kyc.guard_verification_identity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Verification history cannot be deleted'; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."OwnerId" <> OLD."OwnerId" OR NEW."ExternalUserId" <> OLD."ExternalUserId"
                        OR NEW."ConfigurationId" <> OLD."ConfigurationId" OR NEW."Sandbox" <> OLD."Sandbox"
                        OR NEW."ProviderName" <> OLD."ProviderName" OR NEW."AttemptNumber" <> OLD."AttemptNumber"
                        OR OLD."ProviderSessionId" IS NOT NULL AND NEW."ProviderSessionId" IS DISTINCT FROM OLD."ProviderSessionId" THEN
                        RAISE EXCEPTION 'Verification identity is immutable';
                    END IF;
                    RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM kyc.verifications WHERE "ProviderName" <> 'Sumsub' OR "AttemptNumber" <> 1 OR "Status" = 'Expired') THEN
                        RAISE EXCEPTION 'Didit verification history exists; rollback would lose verification data';
                    END IF;
                END $$;
                CREATE OR REPLACE FUNCTION kyc.guard_verification_identity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Verification history cannot be deleted'; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."OwnerId" <> OLD."OwnerId" OR NEW."ExternalUserId" <> OLD."ExternalUserId"
                        OR NEW."LevelName" <> OLD."LevelName" OR NEW."Sandbox" <> OLD."Sandbox"
                        OR OLD."ApplicantId" IS NOT NULL AND NEW."ApplicantId" IS DISTINCT FROM OLD."ApplicantId" THEN
                        RAISE EXCEPTION 'Verification identity is immutable';
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_verifications_ExternalUserId",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropIndex(
                name: "IX_verifications_OwnerId_ProviderName_AttemptNumber",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropIndex(
                name: "IX_verifications_ProviderName_ProviderSessionId",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_kyc_status",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                schema: "kyc",
                table: "verifications");

            migrationBuilder.RenameColumn(
                name: "ProviderSessionId",
                schema: "kyc",
                table: "verifications",
                newName: "ApplicantId");

            migrationBuilder.RenameColumn(
                name: "DecisionReference",
                schema: "kyc",
                table: "verifications",
                newName: "ReviewId");

            migrationBuilder.RenameColumn(
                name: "ConfigurationId",
                schema: "kyc",
                table: "verifications",
                newName: "LevelName");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_ApplicantId",
                schema: "kyc",
                table: "verifications",
                column: "ApplicantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verifications_ExternalUserId",
                schema: "kyc",
                table: "verifications",
                column: "ExternalUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verifications_OwnerId",
                schema: "kyc",
                table: "verifications",
                column: "OwnerId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_kyc_status",
                schema: "kyc",
                table: "verifications",
                sql: "\"Status\" IN ('NotStarted','InProgress','PendingReview','NeedsResubmission','Rejected','Verified')");
        }
    }
}
