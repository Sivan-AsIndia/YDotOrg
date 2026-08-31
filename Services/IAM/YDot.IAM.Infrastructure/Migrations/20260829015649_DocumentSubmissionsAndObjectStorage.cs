using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentSubmissionsAndObjectStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "storage_version_id",
                table: "iam_tenant_documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submission_id",
                table: "iam_tenant_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "iam_tenant_document_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reupload_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_tenant_document_submissions", x => x.id);
                    table.CheckConstraint("ck_iam_tenant_document_submissions_decision_has_notes", "status NOT IN ('Rejected', 'ReuploadRequested') OR decision_notes IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_iam_tenant_document_submissions_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_documents_submission_id",
                table: "iam_tenant_documents",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_document_submissions_status_submitted",
                table: "iam_tenant_document_submissions",
                columns: new[] { "status", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_document_submissions_tenant_type",
                table: "iam_tenant_document_submissions",
                columns: new[] { "tenant_id", "document_type" });

            migrationBuilder.AddForeignKey(
                name: "fk_iam_tenant_documents_tenant_document_submissions_submission",
                table: "iam_tenant_documents",
                column: "submission_id",
                principalTable: "iam_tenant_document_submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_iam_tenant_documents_tenant_document_submissions_submission",
                table: "iam_tenant_documents");

            migrationBuilder.DropTable(
                name: "iam_tenant_document_submissions");

            migrationBuilder.DropIndex(
                name: "ix_iam_tenant_documents_submission_id",
                table: "iam_tenant_documents");

            migrationBuilder.DropColumn(
                name: "storage_version_id",
                table: "iam_tenant_documents");

            migrationBuilder.DropColumn(
                name: "submission_id",
                table: "iam_tenant_documents");
        }
    }
}
