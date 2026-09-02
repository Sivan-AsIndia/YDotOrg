using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDots.DON.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadTemperaturePotentialAndHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Low", NOT "". EF's generated default for a non-nullable string column is an empty
            // string, and an empty string is not a DonationPotential - every existing lead would
            // fail to materialise the moment anything read the queue. The entity's own default is
            // Low, so that is what a row written before this column existed should say.
            migrationBuilder.AddColumn<string>(
                name: "donation_potential",
                table: "don_leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Low");

            migrationBuilder.AddColumn<int>(
                name: "health_score",
                table: "don_leads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // "Cold" for the same reason as donation_potential above: an empty string cannot be
            // read back as a LeadTemperature, and Cold is the entity's default for a lead nobody
            // has formed a view on yet.
            migrationBuilder.AddColumn<string>(
                name: "temperature",
                table: "don_leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Cold");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "donation_potential",
                table: "don_leads");

            migrationBuilder.DropColumn(
                name: "health_score",
                table: "don_leads");

            migrationBuilder.DropColumn(
                name: "temperature",
                table: "don_leads");
        }
    }
}
