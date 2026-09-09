using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MenuOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_iam_menu_definitions_code",
                table: "iam_menu_definitions");

            migrationBuilder.AddColumn<bool>(
                name: "is_system_defined",
                table: "iam_menu_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_tenant_id",
                table: "iam_menu_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_menu_definitions_owner_code",
                table: "iam_menu_definitions",
                columns: new[] { "owner_tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_menu_definitions_owner_status",
                table: "iam_menu_definitions",
                columns: new[] { "owner_tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_menu_definitions_platform_code",
                table: "iam_menu_definitions",
                column: "code",
                unique: true,
                filter: "owner_tenant_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_iam_menu_definitions_owner_code",
                table: "iam_menu_definitions");

            migrationBuilder.DropIndex(
                name: "ix_iam_menu_definitions_owner_status",
                table: "iam_menu_definitions");

            migrationBuilder.DropIndex(
                name: "ix_iam_menu_definitions_platform_code",
                table: "iam_menu_definitions");

            migrationBuilder.DropColumn(
                name: "is_system_defined",
                table: "iam_menu_definitions");

            migrationBuilder.DropColumn(
                name: "owner_tenant_id",
                table: "iam_menu_definitions");

            migrationBuilder.CreateIndex(
                name: "ix_iam_menu_definitions_code",
                table: "iam_menu_definitions",
                column: "code",
                unique: true);
        }
    }
}
