using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_access_review_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_review_count = table.Column<int>(type: "integer", nullable: false),
                    completed_review_count = table.Column<int>(type: "integer", nullable: false),
                    overdue_review_count = table.Column<int>(type: "integer", nullable: false),
                    revoke_on_no_response = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_iam_access_review_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    actor_scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    action_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    client_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    request_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_bulk_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    action_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    source_storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    action_parameters = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    total_item_count = table.Column<int>(type: "integer", nullable: false),
                    processed_item_count = table.Column<int>(type: "integer", nullable: false),
                    succeeded_item_count = table.Column<int>(type: "integer", nullable: false),
                    failed_item_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_item_count = table.Column<int>(type: "integer", nullable: false),
                    validated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failure_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    result_storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
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
                    table.PrimaryKey("pk_iam_bulk_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_business_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    root_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    support_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_culture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    maximum_tenants = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_business_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    parent_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    head_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_iam_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_departments_iam_departments_parent_department_id",
                        column: x => x.parent_department_id,
                        principalTable: "iam_departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_menu_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    parent_menu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    module_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    route = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    required_permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_platform_only = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled_by_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    opens_in_new_tab = table.Column<bool>(type: "boolean", nullable: false),
                    badge_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_menu_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_menu_definitions_iam_menu_definitions_parent_menu_id",
                        column: x => x.parent_menu_id,
                        principalTable: "iam_menu_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_organisation_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    parent_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    state = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    country = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    manager_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_iam_organisation_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_organisation_units_iam_organisation_units_parent_unit_id",
                        column: x => x.parent_unit_id,
                        principalTable: "iam_organisation_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_dead_lettered = table.Column<bool>(type: "boolean", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    module_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    group_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    is_platform_only = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_protected_action_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    draft_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_iam_protected_action_drafts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_bulk_operation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulk_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_identifier = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    source_data = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false),
                    validation_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_processed = table.Column<bool>(type: "boolean", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    was_skipped = table.Column<bool>(type: "boolean", nullable: false),
                    result_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_iam_bulk_operation_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_bulk_operation_items_iam_bulk_operations_bulk_operation",
                        column: x => x.bulk_operation_id,
                        principalTable: "iam_bulk_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    subdomain = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tax_identification_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pan_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    gst_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    organisation_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    established_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    website_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contact_person_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_phone_country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    state = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    country = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_culture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_mfa_requirement = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    maximum_failed_access_attempts = table.Column<int>(type: "integer", nullable: false),
                    lockout_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    password_minimum_length = table.Column<int>(type: "integer", nullable: false),
                    password_expiry_days = table.Column<int>(type: "integer", nullable: false),
                    session_idle_timeout_minutes = table.Column<int>(type: "integer", nullable: false),
                    maximum_users = table.Column<int>(type: "integer", nullable: true),
                    invited_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invitation_accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspension_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resubmission_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_tenants", x => x.id);
                    table.CheckConstraint("ck_iam_tenants_lockout_policy", "maximum_failed_access_attempts >= 1 AND lockout_duration_minutes >= 1");
                    table.CheckConstraint("ck_iam_tenants_password_length", "password_minimum_length >= 6 AND password_minimum_length <= 128");
                    table.CheckConstraint("ck_iam_tenants_rejection_has_reason", "status <> 'Rejected' OR rejection_reason IS NOT NULL");
                    table.CheckConstraint("ck_iam_tenants_suspension_has_reason", "status <> 'Suspended' OR suspension_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_iam_tenants_iam_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalTable: "iam_business_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_menus",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    display_name_override = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    icon_override = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    display_order_override = table.Column<int>(type: "integer", nullable: true),
                    parent_override_menu_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_system_generated = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_iam_tenant_menus", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_tenant_menus_iam_menu_definitions_menu_definition_id",
                        column: x => x.menu_definition_id,
                        principalTable: "iam_menu_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    role_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false),
                    is_default_role = table.Column<bool>(type: "boolean", nullable: false),
                    grants_all_tenant_permissions = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_privileged = table.Column<bool>(type: "boolean", nullable: false),
                    display_tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_roles", x => x.id);
                    table.UniqueConstraint("ak_iam_roles_tenant_key_id", x => new { x.tenant_key, x.id });
                    table.CheckConstraint("ck_iam_roles_platform_has_no_tenant", "(role_type = 'Platform' AND tenant_id IS NULL) OR (role_type <> 'Platform' AND tenant_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_iam_roles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    uploaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issued_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_document_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_iam_tenant_documents", x => x.id);
                    table.CheckConstraint("ck_iam_tenant_documents_rejection_has_notes", "status <> 'Rejected' OR review_notes IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_iam_tenant_documents_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_domains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    domain_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    verification_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_tenant_domains", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_tenant_domains_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    to_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_tenant_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_tenant_status_history_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    first_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    email_confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mobile_country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    mobile_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    account_category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organisation_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    designation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    manager_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    access_starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    access_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_requirement = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    last_signed_in_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    engagement_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    joined_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exited_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    password_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    credential_setup_method = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_locked_out_by_administrator = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_login_user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    last_login_client_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_login_browser = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_login_operating_system = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_login_device_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_failed_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failed_login_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    mfa_enrolled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    authenticator_secret = table.Column<string>(type: "text", nullable: true),
                    recovery_codes_remaining = table.Column<int>(type: "integer", nullable: false),
                    privilege_level = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_super_admin = table.Column<bool>(type: "boolean", nullable: false),
                    is_tenant_admin = table.Column<bool>(type: "boolean", nullable: false),
                    is_system_account = table.Column<bool>(type: "boolean", nullable: false),
                    preferred_culture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_users", x => x.id);
                    table.UniqueConstraint("ak_iam_users_tenant_key_id", x => new { x.tenant_key, x.id });
                    table.CheckConstraint("ck_iam_users_access_window", "access_ends_at_utc IS NULL OR access_ends_at_utc > access_starts_at_utc");
                    table.CheckConstraint("ck_iam_users_manager_not_self", "manager_user_id IS NULL OR manager_user_id <> id");
                    table.CheckConstraint("ck_iam_users_super_admin_has_no_tenant", "(is_super_admin = TRUE AND tenant_id IS NULL)\r\n                  OR (is_super_admin = FALSE AND tenant_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_iam_users_iam_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "iam_departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iam_users_iam_organisation_units_organisation_unit_id",
                        column: x => x.organisation_unit_id,
                        principalTable: "iam_organisation_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iam_users_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iam_users_iam_users_manager_user_id",
                        column: x => x.manager_user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    claim_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_role_claims_iam_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_role_incompatibilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conflicting_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_iam_role_incompatibilities", x => x.id);
                    table.CheckConstraint("ck_iam_role_incompatibilities_not_self", "role_id <> conflicting_role_id");
                    table.ForeignKey(
                        name: "fk_iam_role_incompatibilities_iam_roles_conflicting_role_id",
                        column: x => x.conflicting_role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_iam_role_incompatibilities_iam_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "iam_role_menus",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    is_landing_page = table.Column<bool>(type: "boolean", nullable: false),
                    display_order_override = table.Column<int>(type: "integer", nullable: true),
                    mapped_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mapped_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_iam_role_menus", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_role_menus_iam_menu_definitions_menu_definition_id",
                        column: x => x.menu_definition_id,
                        principalTable: "iam_menu_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_iam_role_menus_iam_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_denied = table.Column<bool>(type: "boolean", nullable: false),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_iam_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_role_permissions_iam_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "iam_permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iam_role_permissions_iam_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_access_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_for_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scope_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    scope_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    business_justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    access_starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    access_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    withdrawn_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    granted_user_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_iam_access_requests", x => x.id);
                    table.CheckConstraint("ck_iam_access_requests_independent_approver", "decided_by_user_id IS NULL OR decided_by_user_id <> requested_by_user_id");
                    table.CheckConstraint("ck_iam_access_requests_justification_length", "length(business_justification) >= 10");
                    table.CheckConstraint("ck_iam_access_requests_window", "access_ends_at_utc IS NULL OR access_ends_at_utc > access_starts_at_utc");
                    table.ForeignKey(
                        name: "fk_iam_access_requests_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_iam_access_requests_asp_net_users_requested_for_user_id",
                        column: x => x.requested_for_user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_access_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    access_snapshot = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    review_due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_decision_applied = table.Column<bool>(type: "boolean", nullable: false),
                    decision_applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminder_count = table.Column<int>(type: "integer", nullable: false),
                    last_reminded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_iam_access_reviews", x => x.id);
                    table.CheckConstraint("ck_iam_access_reviews_decision_has_reason", "decision IS NULL OR decision = 'Retain' OR decision_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_iam_access_reviews_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_iam_access_reviews_asp_net_users_subject_user_id",
                        column: x => x.subject_user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_iam_access_reviews_iam_access_review_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "iam_access_review_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "iam_login_identifier_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_email_change = table.Column<bool>(type: "boolean", nullable: false),
                    current_value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    requested_value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_requested_value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    verification_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    previous_owner_notified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_iam_login_identifier_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_login_identifier_changes_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_mfa_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    masked_destination = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    secret_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("pk_iam_mfa_methods", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_mfa_methods_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    redeemed_from_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    retired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_iam_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_recovery_codes_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_recovery_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_from_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    invalidated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    requested_from_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    requested_user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
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
                    table.PrimaryKey("pk_iam_recovery_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_recovery_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_sign_in_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempted_identifier = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    host_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    outcome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    failure_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    client_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    browser = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    operating_system = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    device_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    attempts_remaining = table.Column<int>(type: "integer", nullable: false),
                    triggered_lockout = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_challenged = table.Column<bool>(type: "boolean", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_sign_in_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_sign_in_attempts_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "iam_trusted_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    device_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    browser = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    operating_system = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    trusted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("pk_iam_trusted_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_trusted_devices_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    claim_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_user_claims_iam_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_data_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scope_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_access_request_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_iam_user_data_scopes", x => x.id);
                    table.CheckConstraint("ck_iam_user_data_scopes_window", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                    table.ForeignKey(
                        name: "fk_iam_user_data_scopes_iam_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    invitation_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    initial_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_from_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    accepted_user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resend_count = table.Column<int>(type: "integer", nullable: false),
                    last_sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invitation_host_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_iam_user_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_user_invitations_iam_roles_initial_role_id",
                        column: x => x.initial_role_id,
                        principalTable: "iam_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_iam_user_invitations_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_iam_user_invitations_iam_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    provider_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_user_logins", x => new { x.login_provider, x.provider_key, x.user_id });
                    table.ForeignKey(
                        name: "fk_iam_user_logins_iam_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source_access_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_user_roles", x => x.id);
                    table.CheckConstraint("ck_iam_user_roles_effective_window", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                    table.ForeignKey(
                        name: "fk_iam_user_roles_iam_roles_tenant_role",
                        columns: x => new { x.tenant_key, x.role_id },
                        principalTable: "iam_roles",
                        principalColumns: new[] { "tenant_key", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iam_user_roles_iam_users_tenant_user",
                        columns: x => new { x.tenant_key, x.user_id },
                        principalTable: "iam_users",
                        principalColumns: new[] { "tenant_key", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operating_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    access_scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    device_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    device_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    browser = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    operating_system = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    mfa_completed = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reauthenticated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_trusted_device = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_iam_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_user_sessions_iam_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_key = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iam_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_iam_user_tokens_iam_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_mfa_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mfa_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    purpose = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    challenge_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    masked_destination = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    maximum_attempts = table.Column<int>(type: "integer", nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
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
                    table.PrimaryKey("pk_iam_mfa_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_mfa_challenges_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_iam_mfa_challenges_mfa_methods_mfa_method_id",
                        column: x => x.mfa_method_id,
                        principalTable: "iam_mfa_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "iam_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_reuse_detected = table.Column<bool>(type: "boolean", nullable: false),
                    created_from_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by_user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
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
                    table.PrimaryKey("pk_iam_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_iam_refresh_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "iam_users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_iam_refresh_tokens_user_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "iam_user_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_requests_number",
                table: "iam_access_requests",
                columns: new[] { "tenant_id", "request_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_requests_role_id",
                table: "iam_access_requests",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_requests_subject",
                table: "iam_access_requests",
                column: "requested_for_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_requests_tenant_status",
                table: "iam_access_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_review_campaigns_code",
                table: "iam_access_review_campaigns",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_reviews_campaign",
                table: "iam_access_reviews",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_reviews_number",
                table: "iam_access_reviews",
                columns: new[] { "tenant_id", "review_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_reviews_reviewer_queue",
                table: "iam_access_reviews",
                columns: new[] { "reviewer_user_id", "status", "review_due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_reviews_role_id",
                table: "iam_access_reviews",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_access_reviews_subject_user_id",
                table: "iam_access_reviews",
                column: "subject_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_action",
                table: "iam_audit_events",
                column: "action_code");

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_actor",
                table: "iam_audit_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_correlation",
                table: "iam_audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_target",
                table: "iam_audit_events",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_tenant_time",
                table: "iam_audit_events",
                columns: new[] { "tenant_id", "occurred_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_iam_bulk_operation_items_row",
                table: "iam_bulk_operation_items",
                columns: new[] { "bulk_operation_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_bulk_operations_number",
                table: "iam_bulk_operations",
                columns: new[] { "tenant_id", "operation_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_bulk_operations_tenant_status",
                table: "iam_bulk_operations",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_business_units_code",
                table: "iam_business_units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_business_units_root_domain",
                table: "iam_business_units",
                column: "root_domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_departments_parent_department_id",
                table: "iam_departments",
                column: "parent_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_departments_tenant_code",
                table: "iam_departments",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_idempotency_records_key",
                table: "iam_idempotency_records",
                columns: new[] { "tenant_id", "endpoint", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_login_identifier_changes_open",
                table: "iam_login_identifier_changes",
                column: "user_id",
                unique: true,
                filter: "status IN ('Draft', 'PendingVerification', 'PendingApproval', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "ix_iam_menu_definitions_code",
                table: "iam_menu_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_menu_definitions_parent_order",
                table: "iam_menu_definitions",
                columns: new[] { "parent_menu_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_mfa_challenges_mfa_method_id",
                table: "iam_mfa_challenges",
                column: "mfa_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_mfa_challenges_token",
                table: "iam_mfa_challenges",
                column: "challenge_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_mfa_challenges_user_purpose",
                table: "iam_mfa_challenges",
                columns: new[] { "user_id", "purpose" },
                filter: "is_consumed = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_iam_mfa_methods_primary",
                table: "iam_mfa_methods",
                column: "user_id",
                unique: true,
                filter: "is_primary = TRUE AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_iam_mfa_methods_user_type",
                table: "iam_mfa_methods",
                columns: new[] { "user_id", "method_type" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_organisation_units_parent_unit_id",
                table: "iam_organisation_units",
                column: "parent_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_organisation_units_tenant_code",
                table: "iam_organisation_units",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_outbox_messages_pending",
                table: "iam_outbox_messages",
                column: "occurred_at_utc",
                filter: "processed_at_utc IS NULL AND is_dead_lettered = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_iam_permissions_code",
                table: "iam_permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_permissions_module_group",
                table: "iam_permissions",
                columns: new[] { "module_code", "group_code" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_protected_action_drafts_token",
                table: "iam_protected_action_drafts",
                column: "draft_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_recovery_codes_user_hash",
                table: "iam_recovery_codes",
                columns: new[] { "user_id", "code_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_recovery_tokens_hash",
                table: "iam_recovery_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_recovery_tokens_user_purpose",
                table: "iam_recovery_tokens",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_refresh_tokens_hash",
                table: "iam_refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_refresh_tokens_session",
                table: "iam_refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_refresh_tokens_user_id",
                table: "iam_refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_claims_role_id",
                table: "iam_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_claims_tenant_role",
                table: "iam_role_claims",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_incompatibilities_conflicting_role_id",
                table: "iam_role_incompatibilities",
                column: "conflicting_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_incompatibilities_role_id",
                table: "iam_role_incompatibilities",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_incompatibilities_unique",
                table: "iam_role_incompatibilities",
                columns: new[] { "tenant_id", "role_id", "conflicting_role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_menus_landing",
                table: "iam_role_menus",
                column: "role_id",
                unique: true,
                filter: "is_landing_page = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_menus_menu_definition_id",
                table: "iam_role_menus",
                column: "menu_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_menus_unique",
                table: "iam_role_menus",
                columns: new[] { "tenant_id", "role_id", "menu_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_permissions_permission_id",
                table: "iam_role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_permissions_role_code",
                table: "iam_role_permissions",
                columns: new[] { "role_id", "permission_code" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_role_permissions_unique",
                table: "iam_role_permissions",
                columns: new[] { "tenant_id", "role_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_roles_business_unit",
                table: "iam_roles",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_roles_tenant_code",
                table: "iam_roles",
                columns: new[] { "tenant_id", "normalized_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_roles_tenant_name",
                table: "iam_roles",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_sign_in_attempts_ip_time",
                table: "iam_sign_in_attempts",
                columns: new[] { "ip_address", "attempted_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_iam_sign_in_attempts_tenant_time",
                table: "iam_sign_in_attempts",
                columns: new[] { "tenant_id", "attempted_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_iam_sign_in_attempts_user_time",
                table: "iam_sign_in_attempts",
                columns: new[] { "user_id", "attempted_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_documents_tenant_type",
                table: "iam_tenant_documents",
                columns: new[] { "tenant_id", "document_type" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_domains_host",
                table: "iam_tenant_domains",
                column: "host_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_domains_primary",
                table: "iam_tenant_domains",
                column: "tenant_id",
                unique: true,
                filter: "is_primary = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_menus_menu_definition_id",
                table: "iam_tenant_menus",
                column: "menu_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_menus_unique",
                table: "iam_tenant_menus",
                columns: new[] { "tenant_id", "menu_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_status_history_tenant_time",
                table: "iam_tenant_status_history",
                columns: new[] { "tenant_id", "occurred_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenants_business_unit_code",
                table: "iam_tenants",
                columns: new[] { "business_unit_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenants_business_unit_subdomain",
                table: "iam_tenants",
                columns: new[] { "business_unit_id", "subdomain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenants_status",
                table: "iam_tenants",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_iam_trusted_devices_token_hash",
                table: "iam_trusted_devices",
                column: "device_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_trusted_devices_user",
                table: "iam_trusted_devices",
                column: "user_id",
                filter: "revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_claims_tenant_user",
                table: "iam_user_claims",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_claims_user_id",
                table: "iam_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_data_scopes_active",
                table: "iam_user_data_scopes",
                columns: new[] { "user_id", "scope_type", "scope_value" },
                unique: true,
                filter: "revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_data_scopes_tenant_user",
                table: "iam_user_data_scopes",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_invitations_initial_role_id",
                table: "iam_user_invitations",
                column: "initial_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_invitations_pending",
                table: "iam_user_invitations",
                column: "user_id",
                unique: true,
                filter: "status IN ('Pending', 'Resent')");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_invitations_reference",
                table: "iam_user_invitations",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_invitations_tenant_id",
                table: "iam_user_invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_invitations_token_hash",
                table: "iam_user_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_logins_tenant_provider",
                table: "iam_user_logins",
                columns: new[] { "tenant_id", "login_provider", "provider_key" },
                unique: true,
                filter: "tenant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_logins_tenant_user",
                table: "iam_user_logins",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_logins_user_id",
                table: "iam_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_roles_active_unique",
                table: "iam_user_roles",
                columns: new[] { "tenant_id", "user_id", "role_id" },
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_roles_tenant_key_role_id",
                table: "iam_user_roles",
                columns: new[] { "tenant_key", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_roles_tenant_key_user_id",
                table: "iam_user_roles",
                columns: new[] { "tenant_key", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_roles_tenant_role",
                table: "iam_user_roles",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_roles_tenant_user",
                table: "iam_user_roles",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_sessions_token_hash",
                table: "iam_user_sessions",
                column: "session_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_user_sessions_user_active",
                table: "iam_user_sessions",
                columns: new[] { "user_id", "expires_at_utc" },
                filter: "revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_business_unit",
                table: "iam_users",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_department_id",
                table: "iam_users",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_global_email",
                table: "iam_users",
                column: "normalized_email",
                unique: true,
                filter: "tenant_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_manager_user_id",
                table: "iam_users",
                column: "manager_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_organisation_unit_id",
                table: "iam_users",
                column: "organisation_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_tenant_code",
                table: "iam_users",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_tenant_email",
                table: "iam_users",
                columns: new[] { "tenant_id", "normalized_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_tenant_status",
                table: "iam_users",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_users_tenant_username",
                table: "iam_users",
                columns: new[] { "tenant_id", "normalized_user_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iam_access_requests");

            migrationBuilder.DropTable(
                name: "iam_access_reviews");

            migrationBuilder.DropTable(
                name: "iam_audit_events");

            migrationBuilder.DropTable(
                name: "iam_bulk_operation_items");

            migrationBuilder.DropTable(
                name: "iam_idempotency_records");

            migrationBuilder.DropTable(
                name: "iam_login_identifier_changes");

            migrationBuilder.DropTable(
                name: "iam_mfa_challenges");

            migrationBuilder.DropTable(
                name: "iam_outbox_messages");

            migrationBuilder.DropTable(
                name: "iam_protected_action_drafts");

            migrationBuilder.DropTable(
                name: "iam_recovery_codes");

            migrationBuilder.DropTable(
                name: "iam_recovery_tokens");

            migrationBuilder.DropTable(
                name: "iam_refresh_tokens");

            migrationBuilder.DropTable(
                name: "iam_role_claims");

            migrationBuilder.DropTable(
                name: "iam_role_incompatibilities");

            migrationBuilder.DropTable(
                name: "iam_role_menus");

            migrationBuilder.DropTable(
                name: "iam_role_permissions");

            migrationBuilder.DropTable(
                name: "iam_sign_in_attempts");

            migrationBuilder.DropTable(
                name: "iam_tenant_documents");

            migrationBuilder.DropTable(
                name: "iam_tenant_domains");

            migrationBuilder.DropTable(
                name: "iam_tenant_menus");

            migrationBuilder.DropTable(
                name: "iam_tenant_status_history");

            migrationBuilder.DropTable(
                name: "iam_trusted_devices");

            migrationBuilder.DropTable(
                name: "iam_user_claims");

            migrationBuilder.DropTable(
                name: "iam_user_data_scopes");

            migrationBuilder.DropTable(
                name: "iam_user_invitations");

            migrationBuilder.DropTable(
                name: "iam_user_logins");

            migrationBuilder.DropTable(
                name: "iam_user_roles");

            migrationBuilder.DropTable(
                name: "iam_user_tokens");

            migrationBuilder.DropTable(
                name: "iam_access_review_campaigns");

            migrationBuilder.DropTable(
                name: "iam_bulk_operations");

            migrationBuilder.DropTable(
                name: "iam_mfa_methods");

            migrationBuilder.DropTable(
                name: "iam_user_sessions");

            migrationBuilder.DropTable(
                name: "iam_permissions");

            migrationBuilder.DropTable(
                name: "iam_menu_definitions");

            migrationBuilder.DropTable(
                name: "iam_roles");

            migrationBuilder.DropTable(
                name: "iam_users");

            migrationBuilder.DropTable(
                name: "iam_departments");

            migrationBuilder.DropTable(
                name: "iam_organisation_units");

            migrationBuilder.DropTable(
                name: "iam_tenants");

            migrationBuilder.DropTable(
                name: "iam_business_units");
        }
    }
}
