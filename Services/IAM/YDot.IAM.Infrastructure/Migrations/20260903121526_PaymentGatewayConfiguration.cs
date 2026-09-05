using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.IAM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentGatewayConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_payment_gateway_config_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    field_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    old_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_iam_payment_gateway_config_audits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_payment_gateway_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    merchant_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    api_key_cipher = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    api_key_hint = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    secret_key_cipher = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    has_secret_key = table.Column<bool>(type: "boolean", nullable: false),
                    webhook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    webhook_secret_cipher = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    has_webhook_secret = table.Column<bool>(type: "boolean", nullable: false),
                    subscribed_events = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    settlement_currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    return_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    payment_link_validity_minutes = table.Column<int>(type: "integer", nullable: false),
                    enabled_methods = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_tested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_iam_payment_gateway_configurations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_iam_payment_gateway_config_audits_configuration",
                table: "iam_payment_gateway_config_audits",
                columns: new[] { "configuration_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_payment_gateway_config_audits_tenant",
                table: "iam_payment_gateway_config_audits",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_payment_gateway_configurations_tenant_active",
                table: "iam_payment_gateway_configurations",
                columns: new[] { "tenant_id", "is_active", "environment" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_payment_gateway_configurations_tenant_provider_env",
                table: "iam_payment_gateway_configurations",
                columns: new[] { "tenant_id", "provider", "environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iam_payment_gateway_config_audits");

            migrationBuilder.DropTable(
                name: "iam_payment_gateway_configurations");
        }
    }
}
