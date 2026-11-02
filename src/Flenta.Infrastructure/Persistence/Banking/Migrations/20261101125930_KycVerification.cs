using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class KycVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kyc");

            migrationBuilder.CreateTable(
                name: "verifications",
                schema: "kyc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LevelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    ApplicantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verifications", x => x.Id);
                    table.UniqueConstraint("AK_verifications_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.CheckConstraint("ck_kyc_status", "\"Status\" IN ('NotStarted','InProgress','PendingReview','NeedsResubmission','Rejected','Verified')");
                    table.CheckConstraint("ck_kyc_verified", "(\"Status\" = 'Verified') = (\"VerifiedAt\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "webhook_receipts",
                schema: "kyc",
                columns: table => new
                {
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_receipts", x => x.Digest);
                });

            migrationBuilder.CreateTable(
                name: "review_events",
                schema: "kyc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_events_verifications_VerificationId_OwnerId",
                        columns: x => new { x.VerificationId, x.OwnerId },
                        principalSchema: "kyc",
                        principalTable: "verifications",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_review_events_OwnerId_CreatedAt",
                schema: "kyc",
                table: "review_events",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_review_events_VerificationId_OwnerId",
                schema: "kyc",
                table: "review_events",
                columns: new[] { "VerificationId", "OwnerId" });

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

            migrationBuilder.CreateIndex(
                name: "IX_webhook_receipts_OwnerId_ReceivedAt",
                schema: "kyc",
                table: "webhook_receipts",
                columns: new[] { "OwnerId", "ReceivedAt" });

            migrationBuilder.Sql("""
                ALTER TABLE kyc.verifications ENABLE ROW LEVEL SECURITY;
                ALTER TABLE kyc.review_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE kyc.webhook_receipts ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON SCHEMA kyc FROM PUBLIC;
                REVOKE ALL ON ALL TABLES IN SCHEMA kyc FROM PUBLIC;
                DO $$ DECLARE client_role text;
                BEGIN
                    FOREACH client_role IN ARRAY ARRAY['anon','authenticated'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = client_role) THEN
                            EXECUTE format('REVOKE ALL ON SCHEMA kyc FROM %I', client_role);
                            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA kyc FROM %I', client_role);
                        END IF;
                    END LOOP;
                END $$;
                CREATE FUNCTION kyc.guard_verification_identity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Verification history cannot be deleted'; END IF;
                    IF NEW."Id" <> OLD."Id" OR NEW."OwnerId" <> OLD."OwnerId" OR NEW."ExternalUserId" <> OLD."ExternalUserId"
                        OR NEW."LevelName" <> OLD."LevelName" OR NEW."Sandbox" <> OLD."Sandbox"
                        OR OLD."ApplicantId" IS NOT NULL AND NEW."ApplicantId" IS DISTINCT FROM OLD."ApplicantId" THEN
                        RAISE EXCEPTION 'Verification identity is immutable';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER immutable_verification_identity BEFORE UPDATE OR DELETE ON kyc.verifications
                    FOR EACH ROW EXECUTE FUNCTION kyc.guard_verification_identity();
                CREATE FUNCTION kyc.reject_history_rewrite() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Verification history is immutable'; END $$;
                CREATE TRIGGER immutable_reviews BEFORE UPDATE OR DELETE ON kyc.review_events
                    FOR EACH ROW EXECUTE FUNCTION kyc.reject_history_rewrite();
                CREATE TRIGGER immutable_receipts BEFORE UPDATE OR DELETE ON kyc.webhook_receipts
                    FOR EACH ROW EXECUTE FUNCTION kyc.reject_history_rewrite();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM kyc.verifications) THEN
                        RAISE EXCEPTION 'Verification history exists; rollback would lose identity verification data';
                    END IF;
                END $$;
                DROP FUNCTION kyc.guard_verification_identity() CASCADE;
                DROP FUNCTION kyc.reject_history_rewrite() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "review_events",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "webhook_receipts",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "verifications",
                schema: "kyc");
        }
    }
}
