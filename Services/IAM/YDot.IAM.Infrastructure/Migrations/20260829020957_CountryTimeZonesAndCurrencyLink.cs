using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CountryTimeZonesAndCurrencyLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_currency_id",
                table: "gm_countries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gm_country_time_zones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_country_time_zones", x => x.id);
                    table.ForeignKey(
                        name: "fk_gm_country_time_zones_gm_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "gm_countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gm_country_time_zones_time_zones_time_zone_id",
                        column: x => x.time_zone_id,
                        principalTable: "gm_time_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gm_countries_default_currency_id",
                table: "gm_countries",
                column: "default_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_gm_country_time_zones_country_order",
                table: "gm_country_time_zones",
                columns: new[] { "country_id", "is_primary", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gm_country_time_zones_scope_pair",
                table: "gm_country_time_zones",
                columns: new[] { "tenant_key", "country_id", "time_zone_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_country_time_zones_zone",
                table: "gm_country_time_zones",
                column: "time_zone_id");

            migrationBuilder.AddForeignKey(
                name: "fk_gm_countries_currencies_default_currency_id",
                table: "gm_countries",
                column: "default_currency_id",
                principalTable: "gm_currencies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_gm_countries_currencies_default_currency_id",
                table: "gm_countries");

            migrationBuilder.DropTable(
                name: "gm_country_time_zones");

            migrationBuilder.DropIndex(
                name: "ix_gm_countries_default_currency_id",
                table: "gm_countries");

            migrationBuilder.DropColumn(
                name: "default_currency_id",
                table: "gm_countries");
        }
    }
}
