using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDots.CAM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "campaign_amount",
                table: "cam_campaigns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_cam_campaigns_campaign_amount",
                table: "cam_campaigns",
                sql: "campaign_amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_cam_campaigns_campaign_amount",
                table: "cam_campaigns");

            migrationBuilder.DropColumn(
                name: "campaign_amount",
                table: "cam_campaigns");
        }
    }
}
