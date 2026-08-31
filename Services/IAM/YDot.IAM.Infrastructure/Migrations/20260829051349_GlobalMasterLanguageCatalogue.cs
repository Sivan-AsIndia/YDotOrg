using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalMasterLanguageCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gm_languages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    culture_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    iso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    iso3 = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    native_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_right_to_left = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_gm_languages", x => x.id);
                    table.CheckConstraint("ck_gm_languages_culture_format", "culture_code ~ '^[a-z]{2}(-[A-Za-z0-9]{2,8})?$'");
                });

            migrationBuilder.CreateTable(
                name: "gm_country_languages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_official = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gm_country_languages", x => x.id);
                    table.ForeignKey(
                        name: "fk_gm_country_languages_gm_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "gm_countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gm_country_languages_languages_language_id",
                        column: x => x.language_id,
                        principalTable: "gm_languages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gm_country_languages_country_order",
                table: "gm_country_languages",
                columns: new[] { "country_id", "is_primary", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gm_country_languages_language",
                table: "gm_country_languages",
                column: "language_id");

            migrationBuilder.CreateIndex(
                name: "ix_gm_country_languages_scope_pair",
                table: "gm_country_languages",
                columns: new[] { "tenant_key", "country_id", "language_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_languages_scope_code",
                table: "gm_languages",
                columns: new[] { "tenant_key", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_languages_scope_culture",
                table: "gm_languages",
                columns: new[] { "tenant_key", "culture_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gm_languages_status_order",
                table: "gm_languages",
                columns: new[] { "status", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gm_country_languages");

            migrationBuilder.DropTable(
                name: "gm_languages");
        }
    }
}
