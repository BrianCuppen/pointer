using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pointer.Core.Data;

#nullable disable

namespace Pointer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "shops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    payment_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    no_show_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    free_cancellation_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shops", x => x.id);
                    table.CheckConstraint("ck_shops_free_cancellation_minutes", "free_cancellation_minutes >= 0");
                    table.CheckConstraint("ck_shops_slug_format", "slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$'");
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    deposit_cents = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.UniqueConstraint("ak_services_shop_id_id", x => new { x.shop_id, x.id });
                    table.CheckConstraint("ck_services_amounts", "price_cents >= 0 AND deposit_cents >= 0");
                    table.CheckConstraint("ck_services_duration", "duration_minutes > 0 AND duration_minutes <= 1440");
                    table.ForeignKey(
                        name: "fk_services_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff", x => x.id);
                    table.UniqueConstraint("ak_staff_shop_id_id", x => new { x.shop_id, x.id });
                    table.ForeignKey(
                        name: "fk_staff_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hold_expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    deposit_cents = table.Column<long>(type: "bigint", nullable: false),
                    no_show_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    free_cancellation_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cancelled_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_amounts", "deposit_cents >= 0 AND free_cancellation_minutes >= 0");
                    table.CheckConstraint("ck_bookings_hold", "status <> 'pending_payment' OR hold_expires_utc IS NOT NULL");
                    table.CheckConstraint("ck_bookings_order", "end_utc > start_utc");
                    table.ForeignKey(
                        name: "fk_bookings_services_shop_id_service_id",
                        columns: x => new { x.shop_id, x.service_id },
                        principalTable: "services",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bookings_staff_shop_id_staff_id",
                        columns: x => new { x.shop_id, x.staff_id },
                        principalTable: "staff",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opening_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    opens = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opening_hours", x => x.id);
                    table.CheckConstraint("ck_opening_hours_order", "closes > opens");
                    table.ForeignKey(
                        name: "fk_opening_hours_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_opening_hours_staff_shop_id_staff_id",
                        columns: x => new { x.shop_id, x.staff_id },
                        principalTable: "staff",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "time_off",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_off", x => x.id);
                    table.CheckConstraint("ck_time_off_order", "end_utc > start_utc");
                    table.ForeignKey(
                        name: "fk_time_off_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_time_off_staff_shop_id_staff_id",
                        columns: x => new { x.shop_id, x.staff_id },
                        principalTable: "staff",
                        principalColumns: new[] { "shop_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    platform_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amounts", "amount_cents >= 0 AND platform_fee_cents >= 0 AND platform_fee_cents <= amount_cents");
                    table.ForeignKey(
                        name: "fk_payments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_shop_id_service_id",
                table: "bookings",
                columns: new[] { "shop_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_shop_id_staff_id",
                table: "bookings",
                columns: new[] { "shop_id", "staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_shop_id_start_utc",
                table: "bookings",
                columns: new[] { "shop_id", "start_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_opening_hours_shop_id_day_of_week",
                table: "opening_hours",
                columns: new[] { "shop_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_opening_hours_shop_id_staff_id",
                table: "opening_hours",
                columns: new[] { "shop_id", "staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_booking_id",
                table: "payments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_provider_payment_id",
                table: "payments",
                columns: new[] { "provider", "provider_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shops_slug",
                table: "shops",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_off_shop_id_staff_id",
                table: "time_off",
                columns: new[] { "shop_id", "staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_time_off_shop_id_start_utc",
                table: "time_off",
                columns: new[] { "shop_id", "start_utc" });

            // No double bookings, enforced by the database (CLAUDE.md hard rule 3).
            // Two bookings for the same staff member may not overlap unless one is cancelled.
            // Ranges are half-open [start, end), so back-to-back bookings are allowed.
            // Concurrent inserts for the same slot: one wins, the other gets SQLSTATE 23P01.
            migrationBuilder.Sql($"""
                ALTER TABLE bookings ADD CONSTRAINT {PointerDbContext.BookingOverlapConstraint}
                EXCLUDE USING gist (
                    staff_id WITH =,
                    tstzrange(start_utc, end_utc, '[)') WITH &&
                ) WHERE (status <> 'cancelled');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"ALTER TABLE bookings DROP CONSTRAINT IF EXISTS {PointerDbContext.BookingOverlapConstraint};");

            migrationBuilder.DropTable(
                name: "opening_hours");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "time_off");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "shops");
        }
    }
}
