using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class DesktopStatementsAndProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Memo",
                schema: "banking",
                table: "transactions",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "AvatarJpeg",
                schema: "banking",
                table: "profile_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedTransactionId",
                schema: "banking",
                table: "admin_audit",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_RelatedTransactionId",
                schema: "banking",
                table: "admin_audit",
                column: "RelatedTransactionId",
                unique: true,
                filter: "\"Action\" = 'TransactionCompensation'");

            migrationBuilder.AddForeignKey(
                name: "FK_admin_audit_transactions_RelatedTransactionId",
                schema: "banking",
                table: "admin_audit",
                column: "RelatedTransactionId",
                principalSchema: "banking",
                principalTable: "transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS(SELECT 1 FROM banking.transactions WHERE "Memo" IS NOT NULL)
                        OR EXISTS(SELECT 1 FROM banking.profile_settings WHERE "AvatarJpeg" IS NOT NULL)
                        OR EXISTS(SELECT 1 FROM banking.admin_audit WHERE "RelatedTransactionId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Desktop data exists; rollback would lose history or profile data';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_admin_audit_transactions_RelatedTransactionId",
                schema: "banking",
                table: "admin_audit");

            migrationBuilder.DropIndex(
                name: "IX_admin_audit_RelatedTransactionId",
                schema: "banking",
                table: "admin_audit");

            migrationBuilder.DropColumn(
                name: "Memo",
                schema: "banking",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "AvatarJpeg",
                schema: "banking",
                table: "profile_settings");

            migrationBuilder.DropColumn(
                name: "RelatedTransactionId",
                schema: "banking",
                table: "admin_audit");
        }
    }
}
