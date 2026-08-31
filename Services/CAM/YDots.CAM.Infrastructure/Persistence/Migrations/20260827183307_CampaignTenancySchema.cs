using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDots.CAM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignTenancySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cam_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cam_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    fund_or_programme = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zip_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    lifecycle_activation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    days_before_start = table.Column<int>(type: "integer", nullable: false),
                    reminder_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    public_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    terms_and_notice = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_cam_campaigns", x => x.id);
                    table.CheckConstraint("ck_cam_campaigns_budget", "budget_amount IS NULL OR budget_amount >= 0");
                    table.CheckConstraint("ck_cam_campaigns_dates", "end_date >= start_date");
                    table.CheckConstraint("ck_cam_campaigns_days_before_start", "days_before_start >= 0");
                    table.CheckConstraint("ck_cam_campaigns_target", "target_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "cam_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cam_mediums",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_mediums", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cam_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cam_campaign_lifecycle_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    effective_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    detailed_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    communication_impact = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    closure_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    action_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_campaign_lifecycle_actions", x => x.id);
                    table.CheckConstraint("ck_cam_lifecycle_actions_approval", "approved_at_utc IS NULL OR approved_by_user_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_cam_campaign_lifecycle_actions_cam_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "cam_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cam_campaign_owners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_campaign_owners", x => x.id);
                    table.ForeignKey(
                        name: "fk_cam_campaign_owners_cam_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "cam_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cam_campaign_readiness_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    success_criteria = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    required_for_launch = table.Column<bool>(type: "boolean", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("pk_cam_campaign_readiness_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_cam_campaign_readiness_checks_cam_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "cam_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cam_campaign_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_campaign_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_cam_campaign_channels_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "cam_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cam_campaign_channels_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "cam_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cam_tracking_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medium_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    active_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generated_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tracking_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    usage_count = table.Column<long>(type: "bigint", nullable: false),
                    total_received = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_cam_tracking_assets", x => x.id);
                    table.CheckConstraint("ck_cam_tracking_assets_submitted", "status IN ('Draft') OR submitted_by_user_id IS NOT NULL");
                    table.CheckConstraint("ck_cam_tracking_assets_usage", "usage_count >= 0 AND total_received >= 0");
                    table.CheckConstraint("ck_cam_tracking_assets_window", "active_to > active_from");
                    table.ForeignKey(
                        name: "fk_cam_tracking_assets_cam_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "cam_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cam_tracking_assets_cam_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "cam_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cam_tracking_assets_cam_mediums_medium_id",
                        column: x => x.medium_id,
                        principalTable: "cam_mediums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cam_tracking_assets_cam_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "cam_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cam_campaign_readiness_blockers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_readiness_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocker_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_cam_campaign_readiness_blockers", x => x.id);
                    table.CheckConstraint("ck_cam_readiness_blockers_resolution", "(is_resolved = false AND resolved_at_utc IS NULL AND resolved_by_user_id IS NULL) OR (is_resolved = true AND resolved_at_utc IS NOT NULL AND resolved_by_user_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_cam_campaign_readiness_blockers_campaign_readiness_checks_c",
                        column: x => x.campaign_readiness_check_id,
                        principalTable: "cam_campaign_readiness_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cam_tracking_asset_places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_tracking_asset_places", x => x.id);
                    table.ForeignKey(
                        name: "fk_cam_tracking_asset_places_cam_tracking_assets_tracking_asse",
                        column: x => x.tracking_asset_id,
                        principalTable: "cam_tracking_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cam_tracking_asset_custom_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_asset_place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cam_tracking_asset_custom_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_cam_tracking_asset_custom_fields_tracking_asset_places_trac",
                        column: x => x.tracking_asset_place_id,
                        principalTable: "cam_tracking_asset_places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cam_audit_events_target",
                table: "cam_audit_events",
                columns: new[] { "target_type", "target_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_audit_events_tenant_time",
                table: "cam_audit_events",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_campaign_channels_channel_id",
                table: "cam_campaign_channels",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_cam_campaign_channels_unique",
                table: "cam_campaign_channels",
                columns: new[] { "campaign_id", "channel_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_lifecycle_actions_campaign_type_status",
                table: "cam_campaign_lifecycle_actions",
                columns: new[] { "campaign_id", "action_type", "action_status" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_lifecycle_actions_pending_close",
                table: "cam_campaign_lifecycle_actions",
                column: "campaign_id",
                unique: true,
                filter: "action_type = 'RequestClose' AND action_status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_cam_campaign_owners_owner",
                table: "cam_campaign_owners",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_cam_campaign_owners_unique",
                table: "cam_campaign_owners",
                columns: new[] { "campaign_id", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_readiness_blockers_open_unique",
                table: "cam_campaign_readiness_blockers",
                column: "campaign_readiness_check_id",
                unique: true,
                filter: "is_resolved = false");

            migrationBuilder.CreateIndex(
                name: "ix_cam_readiness_blockers_owner",
                table: "cam_campaign_readiness_blockers",
                columns: new[] { "owner_user_id", "is_resolved" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_readiness_checks_campaign_name",
                table: "cam_campaign_readiness_checks",
                columns: new[] { "campaign_id", "check_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_readiness_checks_launch_gate",
                table: "cam_campaign_readiness_checks",
                columns: new[] { "campaign_id", "required_for_launch", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_campaigns_tenant_code",
                table: "cam_campaigns",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_campaigns_tenant_status_start",
                table: "cam_campaigns",
                columns: new[] { "tenant_id", "status", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_channels_code",
                table: "cam_channels",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_channels_name",
                table: "cam_channels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_mediums_code",
                table: "cam_mediums",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_mediums_name",
                table: "cam_mediums",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_sources_code",
                table: "cam_sources",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_sources_name",
                table: "cam_sources",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_asset_custom_fields_unique",
                table: "cam_tracking_asset_custom_fields",
                columns: new[] { "tracking_asset_place_id", "field_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_asset_places_asset",
                table: "cam_tracking_asset_places",
                column: "tracking_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_assets_campaign_status",
                table: "cam_tracking_assets",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_assets_channel_id",
                table: "cam_tracking_assets",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_assets_medium_id",
                table: "cam_tracking_assets",
                column: "medium_id");

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_assets_reference",
                table: "cam_tracking_assets",
                column: "tracking_reference",
                unique: true,
                filter: "tracking_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_assets_source_id",
                table: "cam_tracking_assets",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_cam_tracking_assets_tenant_code",
                table: "cam_tracking_assets",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cam_audit_events");

            migrationBuilder.DropTable(
                name: "cam_campaign_channels");

            migrationBuilder.DropTable(
                name: "cam_campaign_lifecycle_actions");

            migrationBuilder.DropTable(
                name: "cam_campaign_owners");

            migrationBuilder.DropTable(
                name: "cam_campaign_readiness_blockers");

            migrationBuilder.DropTable(
                name: "cam_tracking_asset_custom_fields");

            migrationBuilder.DropTable(
                name: "cam_campaign_readiness_checks");

            migrationBuilder.DropTable(
                name: "cam_tracking_asset_places");

            migrationBuilder.DropTable(
                name: "cam_tracking_assets");

            migrationBuilder.DropTable(
                name: "cam_campaigns");

            migrationBuilder.DropTable(
                name: "cam_channels");

            migrationBuilder.DropTable(
                name: "cam_mediums");

            migrationBuilder.DropTable(
                name: "cam_sources");
        }
    }
}
