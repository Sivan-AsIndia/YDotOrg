using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GovernanceHandoverAndStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delegated_at_utc",
                table: "iam_access_reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delegated_by_user_id",
                table: "iam_access_reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delegation_reason",
                table: "iam_access_reviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_reviewer_user_id",
                table: "iam_access_reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "was_escalated",
                table: "iam_access_reviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "return_count",
                table: "iam_access_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "return_reason",
                table: "iam_access_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "returned_at_utc",
                table: "iam_access_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "returned_by_user_id",
                table: "iam_access_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delegated_at_utc",
                table: "iam_access_reviews");

            migrationBuilder.DropColumn(
                name: "delegated_by_user_id",
                table: "iam_access_reviews");

            migrationBuilder.DropColumn(
                name: "delegation_reason",
                table: "iam_access_reviews");

            migrationBuilder.DropColumn(
                name: "original_reviewer_user_id",
                table: "iam_access_reviews");

            migrationBuilder.DropColumn(
                name: "was_escalated",
                table: "iam_access_reviews");

            migrationBuilder.DropColumn(
                name: "return_count",
                table: "iam_access_requests");

            migrationBuilder.DropColumn(
                name: "return_reason",
                table: "iam_access_requests");

            migrationBuilder.DropColumn(
                name: "returned_at_utc",
                table: "iam_access_requests");

            migrationBuilder.DropColumn(
                name: "returned_by_user_id",
                table: "iam_access_requests");
        }
    }
}
