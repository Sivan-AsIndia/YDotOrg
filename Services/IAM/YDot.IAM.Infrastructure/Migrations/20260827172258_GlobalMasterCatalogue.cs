using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalMasterCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gm_countries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    official_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    region = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    iso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    iso3 = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    numeric_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    default_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    has_states = table.Column<bool>(type: "boolean", nullable: false),
                    postal_code_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone_country_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gm_currencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numeric_code = table.Column<int>(type: "integer", nullable: true),
                    currency_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    symbol_position = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    minor_unit_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    rounding_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rounding_step = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_currencies", x => x.id);
                    table.CheckConstraint("ck_gm_currencies_decimal_places", "decimal_places BETWEEN 0 AND 8");
                    table.CheckConstraint("ck_gm_currencies_rounding_step", "rounding_step IS NULL OR rounding_step > 0");
                });

            migrationBuilder.CreateTable(
                name: "gm_time_zones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iana_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    short_name = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    standard_utc_offset_minutes = table.Column<int>(type: "integer", nullable: false),
                    supports_daylight_saving = table.Column<bool>(type: "boolean", nullable: false),
                    daylight_saving_rule_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default_recommended = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_time_zones", x => x.id);
                    table.CheckConstraint("ck_gm_time_zones_dst_note", "supports_daylight_saving OR daylight_saving_rule_note IS NULL");
                    table.CheckConstraint("ck_gm_time_zones_offset_range", "standard_utc_offset_minutes BETWEEN -720 AND 840");
                });

            migrationBuilder.CreateTable(
                name: "gm_state_provinces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    other_jurisdiction_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_federal_jurisdiction = table.Column<bool>(type: "boolean", nullable: false),
                    gst_state_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    state_tax_jurisdiction_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    default_time_zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    postal_code_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_format_hint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_state_provinces", x => x.id);
                    table.CheckConstraint("ck_gm_state_provinces_other_jurisdiction", "jurisdiction_type = 'Other' OR other_jurisdiction_type IS NULL");
                    table.ForeignKey(
                        name: "fk_gm_state_provinces_gm_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "gm_countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gm_state_provinces_time_zones_default_time_zone_id",
                        column: x => x.default_time_zone_id,
                        principalTable: "gm_time_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gm_cities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state_province_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_postal_code_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_metro = table.Column<bool>(type: "boolean", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_cities", x => x.id);
                    table.CheckConstraint("ck_gm_cities_coordinate_range", "latitude IS NULL OR (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)");
                    table.CheckConstraint("ck_gm_cities_coordinates_paired", "(latitude IS NULL) = (longitude IS NULL)");
                    table.ForeignKey(
                        name: "fk_gm_cities_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "gm_countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gm_cities_state_provinces_state_province_id",
                        column: x => x.state_province_id,
                        principalTable: "gm_state_provinces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gm_cities_country",
                table: "gm_cities",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_gm_cities_scope_code",
                table: "gm_cities",
                columns: new[] { "tenant_key", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_cities_state_status",
                table: "gm_cities",
                columns: new[] { "state_province_id", "status", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gm_countries_scope_code",
                table: "gm_countries",
                columns: new[] { "tenant_key", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_countries_scope_iso2",
                table: "gm_countries",
                columns: new[] { "tenant_key", "iso2" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_countries_status_order",
                table: "gm_countries",
                columns: new[] { "status", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gm_currencies_scope_code",
                table: "gm_currencies",
                columns: new[] { "tenant_key", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_currencies_status_order",
                table: "gm_currencies",
                columns: new[] { "status", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gm_state_provinces_country_status",
                table: "gm_state_provinces",
                columns: new[] { "country_id", "status", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gm_state_provinces_default_time_zone_id",
                table: "gm_state_provinces",
                column: "default_time_zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_gm_state_provinces_scope_code",
                table: "gm_state_provinces",
                columns: new[] { "tenant_key", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_time_zones_scope_code",
                table: "gm_time_zones",
                columns: new[] { "tenant_key", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_time_zones_scope_iana",
                table: "gm_time_zones",
                columns: new[] { "tenant_key", "iana_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_time_zones_status_offset",
                table: "gm_time_zones",
                columns: new[] { "status", "standard_utc_offset_minutes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gm_cities");

            migrationBuilder.DropTable(
                name: "gm_currencies");

            migrationBuilder.DropTable(
                name: "gm_state_provinces");

            migrationBuilder.DropTable(
                name: "gm_countries");

            migrationBuilder.DropTable(
                name: "gm_time_zones");
        }
    }
}
