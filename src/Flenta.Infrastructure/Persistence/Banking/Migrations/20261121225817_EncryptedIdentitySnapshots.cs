using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class EncryptedIdentitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_snapshots",
                schema: "kyc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ciphertext = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_snapshots_verifications_VerificationId",
                        column: x => x.VerificationId,
                        principalSchema: "kyc",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_snapshots_VerificationId_CreatedAt_Id",
                schema: "kyc",
                table: "identity_snapshots",
                columns: new[] { "VerificationId", "CreatedAt", "Id" });
            migrationBuilder.Sql("""
                ALTER TABLE kyc.identity_snapshots ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON kyc.identity_snapshots FROM PUBLIC;
                DO $$ DECLARE client_role text; BEGIN
                    FOREACH client_role IN ARRAY ARRAY['anon','authenticated'] LOOP
                        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=client_role) THEN
                            EXECUTE format('REVOKE ALL ON kyc.identity_snapshots FROM %I',client_role);
                        END IF;
                    END LOOP;
                END $$;
                CREATE TRIGGER immutable_identity_snapshots BEFORE UPDATE OR DELETE ON kyc.identity_snapshots
                    FOR EACH ROW EXECUTE FUNCTION banking.reject_admin_history_rewrite();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM kyc.identity_snapshots) THEN
                    RAISE EXCEPTION 'Identity history exists; rollback would lose protected identity data';
                END IF; END $$;
                """);

            migrationBuilder.DropTable(
                name: "identity_snapshots",
                schema: "kyc");
        }
    }
}
