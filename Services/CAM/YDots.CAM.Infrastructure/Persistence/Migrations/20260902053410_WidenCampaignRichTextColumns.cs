using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDots.CAM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenCampaignRichTextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "terms_and_notice",
                table: "cam_campaigns",
                type: "character varying(80000)",
                maxLength: 80000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "public_description",
                table: "cam_campaigns",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "terms_and_notice",
                table: "cam_campaigns",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80000)",
                oldMaxLength: 80000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "public_description",
                table: "cam_campaigns",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000,
                oldNullable: true);
        }
    }
}
