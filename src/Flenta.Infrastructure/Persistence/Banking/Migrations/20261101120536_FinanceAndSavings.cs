using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class FinanceAndSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Flush journal checks from an earlier migration in the same EF transaction before DDL.
            migrationBuilder.Sql("SET CONSTRAINTS ALL IMMEDIATE; SET CONSTRAINTS ALL DEFERRED;");
            migrationBuilder.DropForeignKey(
                name: "FK_entries_accounts_AccountId",
                schema: "banking",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "IX_entries_AccountId",
                schema: "banking",
                table: "entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_owner",
                schema: "banking",
                table: "accounts");

            migrationBuilder.AddColumn<long>(
                name: "CashbackMinorUnits",
                schema: "banking",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "banking",
                table: "transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "UAH");

            migrationBuilder.AddColumn<string>(
                name: "DestinationCurrency",
                schema: "banking",
                table: "transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "banking",
                table: "transactions",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                schema: "banking",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RateDate",
                schema: "banking",
                table: "transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReceivedMinorUnits",
                schema: "banking",
                table: "transactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SavedMinorUnits",
                schema: "banking",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SavingsSkipReason",
                schema: "banking",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "banking",
                table: "entries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "UAH");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "banking",
                table: "accounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "UAH");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_accounts_Id_CurrencyCode",
                schema: "banking",
                table: "accounts",
                columns: new[] { "Id", "CurrencyCode" });

            migrationBuilder.CreateTable(
                name: "exchange_quotes",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DestinationCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    RateDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_quotes", x => x.Id);
                    table.CheckConstraint("ck_exchange_quote", "\"AmountMinorUnits\" > 0 AND \"ReceivedMinorUnits\" > 0 AND \"Rate\" > 0 AND \"SourceCurrency\" <> \"DestinationCurrency\"");
                    table.ForeignKey(
                        name: "FK_exchange_quotes_accounts_DestinationAccountId",
                        column: x => x.DestinationAccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exchange_quotes_accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exchange_quotes_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "banking",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goals",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    RoundUpStep = table.Column<int>(type: "integer", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goals", x => x.Id);
                    table.CheckConstraint("ck_goal_settings", "\"TargetMinorUnits\" > 0 AND \"Percentage\" BETWEEN 0 AND 100 AND \"RoundUpStep\" IN (1,5,10,50,100) AND \"Mode\" IN ('Off','Percentage','RoundUp') AND (\"Mode\" <> 'Percentage' OR \"Percentage\" > 0)");
                    table.ForeignKey(
                        name: "FK_goals_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goals_accounts_FundingAccountId",
                        column: x => x.FundingAccountId,
                        principalSchema: "banking",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "banking",
                table: "accounts",
                columns: new[] { "Id", "BalanceMinorUnits", "CreatedAt", "CurrencyCode", "Iban", "Kind", "OwnerId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000003"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "UAH", null, "SandboxMerchant", null },
                    { new Guid("00000000-0000-0000-0000-000000000011"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", null, "SandboxClearing", null },
                    { new Guid("00000000-0000-0000-0000-000000000012"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", null, "FeeRevenue", null },
                    { new Guid("00000000-0000-0000-0000-000000000013"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", null, "SandboxMerchant", null },
                    { new Guid("00000000-0000-0000-0000-000000000021"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "EUR", null, "SandboxClearing", null },
                    { new Guid("00000000-0000-0000-0000-000000000022"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "EUR", null, "FeeRevenue", null },
                    { new Guid("00000000-0000-0000-0000-000000000023"), 0L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "EUR", null, "SandboxMerchant", null }
                });

            migrationBuilder.Sql("SET CONSTRAINTS ALL IMMEDIATE; SET CONSTRAINTS ALL DEFERRED;");

            migrationBuilder.CreateIndex(
                name: "IX_entries_AccountId_CurrencyCode",
                schema: "banking",
                table: "entries",
                columns: new[] { "AccountId", "CurrencyCode" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_currency",
                schema: "banking",
                table: "accounts",
                sql: "\"CurrencyCode\" IN ('UAH', 'USD', 'EUR')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_owner",
                schema: "banking",
                table: "accounts",
                sql: "(\"Kind\" = 'Customer' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NOT NULL) OR (\"Kind\" = 'Savings' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NULL) OR (\"Kind\" IN ('SandboxClearing', 'FeeRevenue', 'SandboxMerchant') AND \"OwnerId\" IS NULL AND \"Iban\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_quotes_DestinationAccountId",
                schema: "banking",
                table: "exchange_quotes",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_quotes_OwnerId",
                schema: "banking",
                table: "exchange_quotes",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_quotes_SourceAccountId",
                schema: "banking",
                table: "exchange_quotes",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_quotes_TransactionId",
                schema: "banking",
                table: "exchange_quotes",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_goals_AccountId",
                schema: "banking",
                table: "goals",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goals_FundingAccountId",
                schema: "banking",
                table: "goals",
                column: "FundingAccountId",
                unique: true,
                filter: "\"Mode\" <> 'Off' AND NOT \"Archived\"");

            migrationBuilder.CreateIndex(
                name: "IX_goals_OwnerId",
                schema: "banking",
                table: "goals",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_entries_accounts_AccountId_CurrencyCode",
                schema: "banking",
                table: "entries",
                columns: new[] { "AccountId", "CurrencyCode" },
                principalSchema: "banking",
                principalTable: "accounts",
                principalColumns: new[] { "Id", "CurrencyCode" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION banking.check_transaction_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE operation_id uuid; operation_status text; entry_count bigint;
                BEGIN
                    IF TG_TABLE_NAME = 'entries' THEN operation_id := NEW."TransactionId";
                    ELSE operation_id := NEW."Id"; END IF;
                    SELECT "Status" INTO operation_status FROM banking.transactions WHERE "Id" = operation_id;
                    SELECT COUNT(*) INTO entry_count FROM banking.entries WHERE "TransactionId" = operation_id;
                    IF operation_status = 'Completed' THEN
                        IF entry_count < 2 OR EXISTS (
                            SELECT 1 FROM banking.entries WHERE "TransactionId" = operation_id
                            GROUP BY "CurrencyCode" HAVING SUM("AmountMinorUnits"::numeric) <> 0 OR COUNT(*) < 2
                        ) THEN RAISE EXCEPTION 'Unbalanced transaction currency'; END IF;
                        IF EXISTS (
                            SELECT 1 FROM banking.transactions t JOIN banking.accounts a ON a."Id" = t."SourceAccountId"
                            WHERE t."Id" = operation_id AND t."CurrencyCode" <> a."CurrencyCode"
                        ) THEN RAISE EXCEPTION 'Transaction source currency mismatch'; END IF;
                    ELSIF entry_count <> 0 THEN RAISE EXCEPTION 'Only completed transactions may have postings'; END IF;
                    RETURN NULL;
                END $$;

                CREATE FUNCTION banking.guard_account_identity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."Id" IS DISTINCT FROM OLD."Id" OR NEW."OwnerId" IS DISTINCT FROM OLD."OwnerId"
                        OR NEW."CurrencyCode" <> OLD."CurrencyCode" OR NEW."Kind" <> OLD."Kind"
                        OR NEW."Iban" IS DISTINCT FROM OLD."Iban" THEN
                        RAISE EXCEPTION 'Account identity is immutable';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER immutable_account_identity BEFORE UPDATE ON banking.accounts
                    FOR EACH ROW EXECUTE FUNCTION banking.guard_account_identity();

                CREATE FUNCTION banking.guard_goal_accounts() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM banking.accounts s JOIN banking.accounts f ON f."Id" = NEW."FundingAccountId"
                        WHERE s."Id" = NEW."AccountId" AND s."Kind" = 'Savings' AND f."Kind" = 'Customer'
                            AND s."OwnerId" = NEW."OwnerId" AND f."OwnerId" = NEW."OwnerId"
                            AND s."CurrencyCode" = f."CurrencyCode"
                    ) THEN RAISE EXCEPTION 'Invalid goal accounts'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER validate_goal_accounts BEFORE INSERT OR UPDATE ON banking.goals
                    FOR EACH ROW EXECUTE FUNCTION banking.guard_goal_accounts();

                ALTER TABLE banking.goals ENABLE ROW LEVEL SECURITY;
                ALTER TABLE banking.exchange_quotes ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON banking.goals, banking.exchange_quotes FROM PUBLIC;
                DO $$ DECLARE client_role text;
                BEGIN
                    FOREACH client_role IN ARRAY ARRAY['anon', 'authenticated'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = client_role) THEN
                            EXECUTE format('REVOKE ALL ON banking.goals, banking.exchange_quotes FROM %I', client_role);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM banking.goals) OR EXISTS (SELECT 1 FROM banking.exchange_quotes)
                        OR EXISTS (SELECT 1 FROM banking.transactions WHERE "CurrencyCode" <> 'UAH'
                            OR "Kind" NOT IN ('TopUp','InternalTransfer','IbanTransfer'))
                        OR EXISTS (SELECT 1 FROM banking.accounts WHERE "CurrencyCode" <> 'UAH' AND "OwnerId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Finance data exists; rollback would lose financial history';
                    END IF;
                END $$;
                DROP FUNCTION banking.guard_account_identity() CASCADE;
                DROP FUNCTION banking.guard_goal_accounts() CASCADE;
                CREATE OR REPLACE FUNCTION banking.check_transaction_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE operation_id uuid; operation_status text; total numeric; entry_count bigint;
                BEGIN
                    IF TG_TABLE_NAME = 'entries' THEN operation_id := NEW."TransactionId";
                    ELSE operation_id := NEW."Id"; END IF;
                    SELECT "Status" INTO operation_status FROM banking.transactions WHERE "Id" = operation_id;
                    SELECT COALESCE(SUM("AmountMinorUnits"),0), COUNT(*) INTO total, entry_count
                        FROM banking.entries WHERE "TransactionId" = operation_id;
                    IF operation_status = 'Completed' THEN
                        IF total <> 0 OR entry_count < 2 THEN RAISE EXCEPTION 'Unbalanced transaction'; END IF;
                    ELSIF entry_count <> 0 THEN RAISE EXCEPTION 'Only completed transactions may have postings'; END IF;
                    RETURN NULL;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_entries_accounts_AccountId_CurrencyCode",
                schema: "banking",
                table: "entries");

            migrationBuilder.DropTable(
                name: "exchange_quotes",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "goals",
                schema: "banking");

            migrationBuilder.DropIndex(
                name: "IX_entries_AccountId_CurrencyCode",
                schema: "banking",
                table: "entries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_accounts_Id_CurrencyCode",
                schema: "banking",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_currency",
                schema: "banking",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_owner",
                schema: "banking",
                table: "accounts");

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000021"));

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000022"));

            migrationBuilder.DeleteData(
                schema: "banking",
                table: "accounts",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000023"));

            migrationBuilder.DropColumn(
                name: "CashbackMinorUnits",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DestinationCurrency",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "GoalId",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RateDate",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ReceivedMinorUnits",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SavedMinorUnits",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SavingsSkipReason",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "banking",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "banking",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "IX_entries_AccountId",
                schema: "banking",
                table: "entries",
                column: "AccountId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_owner",
                schema: "banking",
                table: "accounts",
                sql: "(\"Kind\" = 'Customer' AND \"OwnerId\" IS NOT NULL AND \"Iban\" IS NOT NULL) OR (\"Kind\" IN ('SandboxClearing', 'FeeRevenue') AND \"OwnerId\" IS NULL AND \"Iban\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_entries_accounts_AccountId",
                schema: "banking",
                table: "entries",
                column: "AccountId",
                principalSchema: "banking",
                principalTable: "accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
