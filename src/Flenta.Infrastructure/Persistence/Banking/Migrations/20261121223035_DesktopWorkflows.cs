using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class DesktopWorkflows : Migration
    {
        // Legacy transactions did not identify a card. Count account purchases conservatively
        // against each existing card; new purchases update only the selected card.
        internal const string LegacyCardSpendingBackfill = """
            UPDATE banking.cards AS c SET
                "SpendingMonth" = EXTRACT(YEAR FROM CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::int * 100
                    + EXTRACT(MONTH FROM CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::int,
                "SpentMinorUnits" = LEAST(COALESCE((
                    SELECT SUM(t."AmountMinorUnits") FROM banking.transactions AS t
                    WHERE t."SourceAccountId" = c."AccountId" AND t."Kind" = 'Purchase' AND t."Status" = 'Completed'
                        AND t."CreatedAt" >= date_trunc('month', CURRENT_TIMESTAMP AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
                        AND t."CreatedAt" < (date_trunc('month', CURRENT_TIMESTAMP AT TIME ZONE 'UTC') + INTERVAL '1 month') AT TIME ZONE 'UTC'
                ), 0), 9223372036854775807)::bigint;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdminFrozen",
                schema: "banking",
                table: "cards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CashWithdrawals",
                schema: "banking",
                table: "cards",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContactlessPayments",
                schema: "banking",
                table: "cards",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyLimitMinorUnits",
                schema: "banking",
                table: "cards",
                type: "bigint",
                nullable: false,
                defaultValue: 1000000000000L);

            migrationBuilder.AddColumn<bool>(
                name: "OnlinePayments",
                schema: "banking",
                table: "cards",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SpendingMonth",
                schema: "banking",
                table: "cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SpentMinorUnits",
                schema: "banking",
                table: "cards",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "Blocked",
                schema: "banking",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "admin_audit",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_audit_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "banking",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_controls",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Blocked = table.Column<bool>(type: "boolean", nullable: false),
                    CardsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_controls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profile_settings",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TransactionNotifications = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saved_recipients",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Iban = table.Column<string>(type: "character varying(29)", maxLength: 29, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_recipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "support_requests",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "manual_reviews",
                schema: "kyc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_manual_reviews_admin_audit_AuditId",
                        column: x => x.AuditId,
                        principalSchema: "banking",
                        principalTable: "admin_audit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_manual_reviews_verifications_VerificationId",
                        column: x => x.VerificationId,
                        principalSchema: "kyc",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_receipts",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_receipts_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalSchema: "banking",
                        principalTable: "notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsStaff = table.Column<bool>(type: "boolean", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_messages_support_requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "banking",
                        principalTable: "support_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000011"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000012"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000013"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000021"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000022"),
                column: "Blocked",
                value: false);

            migrationBuilder.UpdateData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000023"),
                column: "Blocked",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_ActorId_IdempotencyKey",
                schema: "banking",
                table: "admin_audit",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_TargetId_CreatedAt_Id",
                schema: "banking",
                table: "admin_audit",
                columns: new[] { "TargetId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_TransactionId",
                schema: "banking",
                table: "admin_audit",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_manual_reviews_AuditId",
                schema: "kyc",
                table: "manual_reviews",
                column: "AuditId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manual_reviews_VerificationId_CreatedAt_Id",
                schema: "kyc",
                table: "manual_reviews",
                columns: new[] { "VerificationId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_receipts_NotificationId",
                schema: "banking",
                table: "notification_receipts",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_receipts_OwnerId_NotificationId",
                schema: "banking",
                table: "notification_receipts",
                columns: new[] { "OwnerId", "NotificationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_OwnerId_CreatedAt_Id",
                schema: "banking",
                table: "notifications",
                columns: new[] { "OwnerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_recipients_OwnerId_Iban",
                schema: "banking",
                table: "saved_recipients",
                columns: new[] { "OwnerId", "Iban" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_messages_RequestId_CreatedAt_Id",
                schema: "banking",
                table: "support_messages",
                columns: new[] { "RequestId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_OwnerId_Status_UpdatedAt",
                schema: "banking",
                table: "support_requests",
                columns: new[] { "OwnerId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_Status_UpdatedAt",
                schema: "banking",
                table: "support_requests",
                columns: new[] { "Status", "UpdatedAt" });
            migrationBuilder.Sql(LegacyCardSpendingBackfill);
            migrationBuilder.Sql("""
                DO $$ DECLARE table_name text; client_role text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY['profile_settings','saved_recipients','support_requests','support_messages','notifications','notification_receipts','admin_audit','client_controls'] LOOP
                        EXECUTE format('ALTER TABLE banking.%I ENABLE ROW LEVEL SECURITY',table_name);
                        EXECUTE format('REVOKE ALL ON banking.%I FROM PUBLIC',table_name);
                        FOREACH client_role IN ARRAY ARRAY['anon','authenticated'] LOOP
                            IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=client_role) THEN
                                EXECUTE format('REVOKE ALL ON banking.%I FROM %I',table_name,client_role);
                            END IF;
                        END LOOP;
                    END LOOP;
                END $$;
                ALTER TABLE kyc.manual_reviews ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON kyc.manual_reviews FROM PUBLIC;
                DO $$ DECLARE client_role text; BEGIN
                    FOREACH client_role IN ARRAY ARRAY['anon','authenticated'] LOOP
                        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=client_role) THEN
                            EXECUTE format('REVOKE ALL ON kyc.manual_reviews FROM %I',client_role);
                        END IF;
                    END LOOP;
                END $$;
                CREATE FUNCTION banking.reject_admin_history_rewrite() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Administrative history is immutable'; END $$;
                CREATE TRIGGER immutable_admin_audit BEFORE UPDATE OR DELETE ON banking.admin_audit
                    FOR EACH ROW EXECUTE FUNCTION banking.reject_admin_history_rewrite();
                CREATE TRIGGER immutable_manual_reviews BEFORE UPDATE OR DELETE ON kyc.manual_reviews
                    FOR EACH ROW EXECUTE FUNCTION banking.reject_admin_history_rewrite();
                CREATE TRIGGER immutable_support_messages BEFORE UPDATE OR DELETE ON banking.support_messages
                    FOR EACH ROW EXECUTE FUNCTION banking.reject_admin_history_rewrite();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS(SELECT 1 FROM banking.admin_audit) OR EXISTS(SELECT 1 FROM banking.support_requests)
                        OR EXISTS(SELECT 1 FROM banking.profile_settings) OR EXISTS(SELECT 1 FROM banking.saved_recipients)
                        OR EXISTS(SELECT 1 FROM banking.notifications) OR EXISTS(SELECT 1 FROM banking.client_controls)
                        OR EXISTS(SELECT 1 FROM banking.cards WHERE "SpentMinorUnits" > 0 OR "AdminFrozen")
                        OR EXISTS(SELECT 1 FROM banking.accounts WHERE "Blocked") THEN
                        RAISE EXCEPTION 'Desktop data exists; rollback would lose user or audit data';
                    END IF;
                END $$;
                DROP FUNCTION banking.reject_admin_history_rewrite() CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "client_controls",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "manual_reviews",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "notification_receipts",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "profile_settings",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "saved_recipients",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "support_messages",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "admin_audit",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "support_requests",
                schema: "banking");

            migrationBuilder.DropColumn(
                name: "AdminFrozen",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "CashWithdrawals",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "ContactlessPayments",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "MonthlyLimitMinorUnits",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "OnlinePayments",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "SpendingMonth",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "SpentMinorUnits",
                schema: "banking",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "Blocked",
                schema: "banking",
                table: "accounts");
        }
    }
}
