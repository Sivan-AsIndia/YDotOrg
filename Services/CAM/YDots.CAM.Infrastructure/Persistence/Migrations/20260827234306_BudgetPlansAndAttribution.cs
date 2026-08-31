using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDots.CAM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetPlansAndAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cam_attribution_correction_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_tracking_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_tracking_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attribution_changed = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_cam_attribution_correction_requests", x => x.id);
                    table.CheckConstraint("ck_cam_attribution_corrections_changed", "is_resolved = true OR attribution_changed = false");
                    table.CheckConstraint("ck_cam_attribution_corrections_resolution", "(is_resolved = false AND resolved_at_utc IS NULL AND resolved_by_user_id IS NULL) OR (is_resolved = true AND resolved_at_utc IS NOT NULL AND resolved_by_user_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "cam_budget_target_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_period = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_dimension = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_cam_budget_target_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_cam_budget_target_plans_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "cam_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cam_budget_target_plan_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_target_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    budget_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    expected_volume = table.Column<int>(type: "integer", nullable: false),
                    assumptions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    approval_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    effective_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_budget_target_plan_versions", x => x.id);
                    table.CheckConstraint("ck_cam_budget_plan_versions_amounts", "target_amount >= 0 AND budget_amount >= 0 AND expected_volume >= 0");
                    table.CheckConstraint("ck_cam_budget_plan_versions_approval", "(approved_by_user_id IS NULL AND approved_at_utc IS NULL) OR (approved_by_user_id IS NOT NULL AND approved_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_cam_budget_plan_versions_submission", "(submitted_by_user_id IS NULL AND submitted_at_utc IS NULL) OR (submitted_by_user_id IS NOT NULL AND submitted_at_utc IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_cam_budget_target_plan_versions_cam_budget_target_plans_bud",
                        column: x => x.budget_target_plan_id,
                        principalTable: "cam_budget_target_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cam_attribution_corrections_queue",
                table: "cam_attribution_correction_requests",
                columns: new[] { "tenant_id", "is_resolved" });

            migrationBuilder.CreateIndex(
                name: "ux_cam_attribution_corrections_open",
                table: "cam_attribution_correction_requests",
                column: "donation_id",
                unique: true,
                filter: "is_resolved = false");

            migrationBuilder.CreateIndex(
                name: "ux_cam_budget_plan_versions_approved",
                table: "cam_budget_target_plan_versions",
                column: "budget_target_plan_id",
                unique: true,
                filter: "approval_state = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ux_cam_budget_plan_versions_number",
                table: "cam_budget_target_plan_versions",
                columns: new[] { "budget_target_plan_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_budget_plans_owner",
                table: "cam_budget_target_plans",
                columns: new[] { "tenant_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cam_budget_plans_code",
                table: "cam_budget_target_plans",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cam_budget_plans_dimension",
                table: "cam_budget_target_plans",
                columns: new[] { "campaign_id", "plan_period", "target_dimension" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cam_attribution_correction_requests");

            migrationBuilder.DropTable(
                name: "cam_budget_target_plan_versions");

            migrationBuilder.DropTable(
                name: "cam_budget_target_plans");
        }
    }
}
