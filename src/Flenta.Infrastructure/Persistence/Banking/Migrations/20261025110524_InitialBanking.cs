using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class InitialBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "banking");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Iban = table.Column<string>(type: "character varying(29)", maxLength: 29, nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BalanceMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                    table.CheckConstraint("ck_account_balance", "\"Kind\" = 'SandboxClearing' OR \"BalanceMinorUnits\" >= 0");
                    table.CheckConstraint("ck_account_owner", "(\"Kind\" = 'Customer' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NOT NULL) OR (\"Kind\" IN ('SandboxClearing', 'FeeRevenue') AND \"OwnerId\" IS NULL AND \"Iban\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "cards",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    ProtectedData = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiryMonth = table.Column<int>(type: "integer", nullable: false),
                    ExpiryYear = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSecretsViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cards", x => x.Id);
                    table.CheckConstraint("ck_card_last4", "\"Last4\" ~ '^[0-9]{4}$'");
                    table.CheckConstraint("ck_card_status", "\"Status\" IN ('Active', 'Blocked', 'Closed')");
                    table.ForeignKey(
                        name: "FK_cards_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationIban = table.Column<string>(type: "character varying(29)", maxLength: 29, nullable: true),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    FeeMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.CheckConstraint("ck_transaction_amount", "\"AmountMinorUnits\" > 0 AND \"FeeMinorUnits\" >= 0");
                    table.CheckConstraint("ck_transaction_status", "\"Status\" IN ('Pending', 'Completed', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_transactions_accounts_DestinationAccountId",
                        column: x => x.DestinationAccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entries", x => x.Id);
                    table.CheckConstraint("ck_entry_nonzero", "\"AmountMinorUnits\" <> 0");
                    table.ForeignKey(
                        name: "FK_entries_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_entries_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "banking",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "banking",
                table: "accounts",
                columns: new[] { "Id", "BalanceMinorUnits", "CreatedAt", "Iban", "Kind", "OwnerId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "SandboxClearing", null },
                    { new Guid("00000000-0000-0000-0000-000000000002"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "FeeRevenue", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Iban",
                schema: "banking",
                table: "accounts",
                column: "Iban",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_OwnerId",
                schema: "banking",
                table: "accounts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_cards_AccountId",
                schema: "banking",
                table: "cards",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_cards_Fingerprint",
                schema: "banking",
                table: "cards",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entries_AccountId",
                schema: "banking",
                table: "entries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_entries_TransactionId_AccountId",
                schema: "banking",
                table: "entries",
                columns: new[] { "TransactionId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_DestinationAccountId",
                schema: "banking",
                table: "transactions",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_InitiatorId_CreatedAt",
                schema: "banking",
                table: "transactions",
                columns: new[] { "InitiatorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_InitiatorId_IdempotencyKey",
                schema: "banking",
                table: "transactions",
                columns: new[] { "InitiatorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_SourceAccountId",
                schema: "banking",
                table: "transactions",
                column: "SourceAccountId");

            migrationBuilder.Sql("""
                CREATE FUNCTION banking.reject_ledger_rewrite() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Ledger entries are immutable';
                END $$;
                CREATE TRIGGER immutable_entries BEFORE UPDATE OR DELETE ON banking.entries
                    FOR EACH ROW EXECUTE FUNCTION banking.reject_ledger_rewrite();

                CREATE FUNCTION banking.guard_transaction_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Transaction history cannot be deleted'; END IF;
                    IF OLD."Status" <> 'Pending' AND NEW IS DISTINCT FROM OLD THEN
                        RAISE EXCEPTION 'Finalized transactions are immutable';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER immutable_transactions BEFORE UPDATE OR DELETE ON banking.transactions
                    FOR EACH ROW EXECUTE FUNCTION banking.guard_transaction_history();

                CREATE FUNCTION banking.check_transaction_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE operation_id uuid; operation_status text; total numeric; entry_count bigint;
                BEGIN
                    IF TG_TABLE_NAME = 'entries' THEN operation_id := NEW."TransactionId";
                    ELSE operation_id := NEW."Id"; END IF;
                    SELECT "Status" INTO operation_status FROM banking.transactions WHERE "Id" = operation_id;
                    SELECT COALESCE(SUM("AmountMinorUnits"), 0), COUNT(*) INTO total, entry_count
                        FROM banking.entries WHERE "TransactionId" = operation_id;
                    IF operation_status = 'Completed' THEN
                        IF total <> 0 OR entry_count < 2 THEN RAISE EXCEPTION 'Unbalanced transaction'; END IF;
                    ELSIF entry_count <> 0 THEN RAISE EXCEPTION 'Only completed transactions may have postings';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER balanced_transaction AFTER INSERT OR UPDATE ON banking.transactions
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION banking.check_transaction_integrity();
                CREATE CONSTRAINT TRIGGER balanced_entries AFTER INSERT ON banking.entries
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION banking.check_transaction_integrity();

                CREATE FUNCTION banking.check_account_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE account_id uuid; balance bigint; posted numeric;
                BEGIN
                    IF TG_TABLE_NAME = 'entries' THEN account_id := NEW."AccountId";
                    ELSE account_id := NEW."Id"; END IF;
                    SELECT "BalanceMinorUnits" INTO balance FROM banking.accounts WHERE "Id" = account_id;
                    SELECT COALESCE(SUM("AmountMinorUnits"), 0) INTO posted FROM banking.entries WHERE "AccountId" = account_id;
                    IF balance <> posted THEN RAISE EXCEPTION 'Balance does not match journal'; END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER account_matches_journal AFTER INSERT OR UPDATE ON banking.accounts
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION banking.check_account_integrity();
                CREATE CONSTRAINT TRIGGER posting_matches_balance AFTER INSERT ON banking.entries
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION banking.check_account_integrity();

                ALTER TABLE banking.accounts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banking.cards ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banking.transactions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banking.entries ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON SCHEMA banking FROM PUBLIC;
                REVOKE ALL ON ALL TABLES IN SCHEMA banking FROM PUBLIC;
                DO $$ DECLARE client_role text;
                BEGIN
                    FOREACH client_role IN ARRAY ARRAY['anon', 'authenticated'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = client_role) THEN
                            EXECUTE format('REVOKE ALL ON SCHEMA banking FROM %I', client_role);
                            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA banking FROM %I', client_role);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION banking.reject_ledger_rewrite() CASCADE;
                DROP FUNCTION banking.guard_transaction_history() CASCADE;
                DROP FUNCTION banking.check_transaction_integrity() CASCADE;
                DROP FUNCTION banking.check_account_integrity() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "cards",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "entries",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "banking");
        }
    }
}
