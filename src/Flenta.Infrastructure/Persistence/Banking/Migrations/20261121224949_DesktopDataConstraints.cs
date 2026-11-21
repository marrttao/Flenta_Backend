using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flenta.Infrastructure.Persistence.Banking.Migrations
{
    /// <inheritdoc />
    public partial class DesktopDataConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_receipts_notifications_NotificationId",
                schema: "banking",
                table: "notification_receipts");

            migrationBuilder.DropIndex(
                name: "IX_notification_receipts_NotificationId",
                schema: "banking",
                table: "notification_receipts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_notifications_Id_OwnerId",
                schema: "banking",
                table: "notifications",
                columns: new[] { "Id", "OwnerId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_support_state",
                schema: "banking",
                table: "support_requests",
                sql: "\"Status\" IN ('Open','Closed') AND \"Kind\" IN ('Message','Callback')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_avatar",
                schema: "banking",
                table: "profile_settings",
                sql: "\"AvatarJpeg\" IS NULL OR octet_length(\"AvatarJpeg\") <= 262144");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_currency",
                schema: "banking",
                table: "profile_settings",
                sql: "\"DefaultCurrency\" IN ('UAH','USD','EUR')");

            migrationBuilder.CreateIndex(
                name: "IX_notification_receipts_NotificationId_OwnerId",
                schema: "banking",
                table: "notification_receipts",
                columns: new[] { "NotificationId", "OwnerId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_card_limits",
                schema: "banking",
                table: "cards",
                sql: "\"MonthlyLimitMinorUnits\" > 0 AND \"MonthlyLimitMinorUnits\" <= 1000000000000 AND \"SpentMinorUnits\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_receipts_notifications_NotificationId_OwnerId",
                schema: "banking",
                table: "notification_receipts",
                columns: new[] { "NotificationId", "OwnerId" },
                principalSchema: "banking",
                principalTable: "notifications",
                principalColumns: new[] { "Id", "OwnerId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_receipts_notifications_NotificationId_OwnerId",
                schema: "banking",
                table: "notification_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_support_state",
                schema: "banking",
                table: "support_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_avatar",
                schema: "banking",
                table: "profile_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_currency",
                schema: "banking",
                table: "profile_settings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_notifications_Id_OwnerId",
                schema: "banking",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notification_receipts_NotificationId_OwnerId",
                schema: "banking",
                table: "notification_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_card_limits",
                schema: "banking",
                table: "cards");

            migrationBuilder.CreateIndex(
                name: "IX_notification_receipts_NotificationId",
                schema: "banking",
                table: "notification_receipts",
                column: "NotificationId");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_receipts_notifications_NotificationId",
                schema: "banking",
                table: "notification_receipts",
                column: "NotificationId",
                principalSchema: "banking",
                principalTable: "notifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
