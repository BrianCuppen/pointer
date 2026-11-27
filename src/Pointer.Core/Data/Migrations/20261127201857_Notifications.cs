using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pointer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "shops",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "no_show_notice_enabled",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "owner_daily_overview",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "owner_email",
                table: "shops",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "owner_instant_emails",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "reminder_hours_before",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<string>(
                name: "cancel_token",
                table: "bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "md5(random()::text) || md5(random()::text)");

            // The default only backfills existing rows; new tokens come from the application.
            migrationBuilder.Sql("ALTER TABLE bookings ALTER COLUMN cancel_token DROP DEFAULT;");

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    overview_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    not_before_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_shops_reminder_hours",
                table: "shops",
                sql: "reminder_hours_before BETWEEN 2 AND 48");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_cancel_token",
                table: "bookings",
                column: "cancel_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_booking_id_type",
                table: "notifications",
                columns: new[] { "booking_id", "type" },
                unique: true,
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_shop_id_type_overview_date",
                table: "notifications",
                columns: new[] { "shop_id", "type", "overview_date" },
                unique: true,
                filter: "overview_date IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_created_utc",
                table: "notifications",
                columns: new[] { "status", "created_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_shops_reminder_hours",
                table: "shops");

            migrationBuilder.DropIndex(
                name: "ix_bookings_cancel_token",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "address",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "no_show_notice_enabled",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "owner_daily_overview",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "owner_email",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "owner_instant_emails",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "reminder_hours_before",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "cancel_token",
                table: "bookings");
        }
    }
}
