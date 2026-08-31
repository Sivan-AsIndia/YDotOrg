using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDot.PAY.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pay_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    target_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pay_donation_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalised_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_identifier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    country_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consent_given = table.Column<bool>(type: "boolean", nullable: false),
                    consent_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_given_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    allow_public_recognition = table.Column<bool>(type: "boolean", nullable: false),
                    public_recognition_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payment_link_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    payment_link_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    existing_donor_matched = table.Column<bool>(type: "boolean", nullable: true),
                    existing_donor_checked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_pay_donation_intents", x => x.id);
                    table.CheckConstraint("ck_pay_donation_intents_amount", "amount > 0");
                    table.CheckConstraint("ck_pay_donation_intents_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_pay_donation_intents_consent", "consent_given = false OR consent_given_at_utc IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "pay_gateway_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    merchant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    api_key_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    webhook_secret_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_test_mode = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    settlement_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    return_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    webhook_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    payment_link_validity_minutes = table.Column<int>(type: "integer", nullable: false),
                    enabled_methods = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_pay_gateway_accounts", x => x.id);
                    table.CheckConstraint("ck_pay_gateway_accounts_link_validity", "payment_link_validity_minutes > 0 AND payment_link_validity_minutes <= 43200");
                });

            migrationBuilder.CreateTable(
                name: "pay_receipt_number_counters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_receipt_number_counters", x => x.id);
                    table.CheckConstraint("ck_pay_receipt_number_counters_last", "last_number >= 0");
                });

            migrationBuilder.CreateTable(
                name: "pay_payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    gateway_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gateway_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    method_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    masked_instrument = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    requested_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    requested_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    captured_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    captured_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gateway_result_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gateway_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    donor_facing_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    donor_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    donor_user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
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
                    table.PrimaryKey("pk_pay_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_pay_payment_attempts_number", "attempt_number > 0");
                    table.ForeignKey(
                        name: "fk_pay_payment_attempts_pay_donation_intents_donation_intent_id",
                        column: x => x.donation_intent_id,
                        principalTable: "pay_donation_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pay_donations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    donation_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    gateway_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    gateway_fee_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    net_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    refunded_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    refunded_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    donor_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    donor_mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    donor_tax_identifier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    donor_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    donated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    method_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    gateway_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    settlement_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settlement_batch_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reconciliation_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reconciled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reconciliation_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tracking_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_pay_donations", x => x.id);
                    table.CheckConstraint("ck_pay_donations_amount", "amount > 0");
                    table.CheckConstraint("ck_pay_donations_fee", "gateway_fee IS NULL OR gateway_fee >= 0");
                    table.CheckConstraint("ck_pay_donations_refunded", "refunded_amount >= 0 AND refunded_amount <= amount");
                    table.ForeignKey(
                        name: "fk_pay_donations_donation_intents_donation_intent_id",
                        column: x => x.donation_intent_id,
                        principalTable: "pay_donation_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pay_donations_payment_attempts_payment_attempt_id",
                        column: x => x.payment_attempt_id,
                        principalTable: "pay_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_payment_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donation_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    gateway_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    gateway_event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gateway_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw_payload = table.Column<string>(type: "text", nullable: true),
                    signature_verified = table.Column<bool>(type: "boolean", nullable: false),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    processing_attempts = table.Column<int>(type: "integer", nullable: false),
                    dismissed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dismissal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_pay_payment_events", x => x.id);
                    table.CheckConstraint("ck_pay_payment_events_attempts", "processing_attempts >= 0");
                    table.ForeignKey(
                        name: "fk_pay_payment_events_pay_payment_attempts_payment_attempt_id",
                        column: x => x.payment_attempt_id,
                        principalTable: "pay_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pay_chargeback_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    gateway_dispute_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reason_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    disputed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    disputed_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    chargeback_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    chargeback_fee_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidence_submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidence_submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    evidence_document_urls = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_pay_chargeback_cases", x => x.id);
                    table.CheckConstraint("ck_pay_chargeback_cases_amount", "disputed_amount > 0");
                    table.CheckConstraint("ck_pay_chargeback_cases_evidence", "evidence_submitted_at_utc IS NULL OR evidence_submitted_by_user_id IS NOT NULL");
                    table.CheckConstraint("ck_pay_chargeback_cases_fee", "chargeback_fee IS NULL OR chargeback_fee >= 0");
                    table.ForeignKey(
                        name: "fk_pay_chargeback_cases_donations_donation_id",
                        column: x => x.donation_id,
                        principalTable: "pay_donations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supersedes_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    financial_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    donor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    donor_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    donor_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    donor_tax_identifier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    campaign_or_fund_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    organisation_tax_reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_exemption_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correction_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    document_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_pay_receipts", x => x.id);
                    table.CheckConstraint("ck_pay_receipts_amount", "amount > 0");
                    table.CheckConstraint("ck_pay_receipts_issued", "status <> 'Issued' OR (receipt_number IS NOT NULL AND issued_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_pay_receipts_version_number", "version_number > 0");
                    table.CheckConstraint("ck_pay_receipts_voided", "voided_at_utc IS NULL OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_pay_receipts_pay_donations_donation_id",
                        column: x => x.donation_id,
                        principalTable: "pay_donations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pay_receipts_pay_receipts_supersedes_receipt_id",
                        column: x => x.supersedes_receipt_id,
                        principalTable: "pay_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_refund_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason_detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    gateway_refund_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gateway_failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    receipt_corrected = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_pay_refund_cases", x => x.id);
                    table.CheckConstraint("ck_pay_refund_cases_amount", "amount > 0");
                    table.CheckConstraint("ck_pay_refund_cases_decision", "decided_at_utc IS NULL OR decided_by_user_id IS NOT NULL");
                    table.CheckConstraint("ck_pay_refund_cases_rejection", "status <> 'Rejected' OR rejection_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_pay_refund_cases_pay_donations_donation_id",
                        column: x => x.donation_id,
                        principalTable: "pay_donations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_receipt_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    destination = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    attempted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_pay_receipt_deliveries", x => x.id);
                    table.CheckConstraint("ck_pay_receipt_deliveries_failure", "status <> 'Failed' OR failure_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_pay_receipt_deliveries_pay_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "pay_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pay_audit_events_action",
                table: "pay_audit_events",
                columns: new[] { "action_code", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_audit_events_correlation",
                table: "pay_audit_events",
                column: "correlation_id",
                filter: "correlation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_audit_events_result",
                table: "pay_audit_events",
                columns: new[] { "result", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_audit_events_target",
                table: "pay_audit_events",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_audit_events_tenant_occurred",
                table: "pay_audit_events",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_chargeback_cases_assignee",
                table: "pay_chargeback_cases",
                column: "assigned_to_user_id",
                filter: "assigned_to_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_chargeback_cases_donation",
                table: "pay_chargeback_cases",
                column: "donation_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_chargeback_cases_tenant_due",
                table: "pay_chargeback_cases",
                columns: new[] { "tenant_id", "status", "evidence_due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_pay_chargeback_cases_dispute",
                table: "pay_chargeback_cases",
                column: "gateway_dispute_reference",
                unique: true,
                filter: "gateway_dispute_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pay_chargeback_cases_reference",
                table: "pay_chargeback_cases",
                columns: new[] { "tenant_id", "case_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_donation_intents_campaign",
                table: "pay_donation_intents",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_donation_intents_lead",
                table: "pay_donation_intents",
                column: "lead_id",
                filter: "lead_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_donation_intents_reference",
                table: "pay_donation_intents",
                column: "intent_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_donation_intents_tenant_email",
                table: "pay_donation_intents",
                columns: new[] { "tenant_id", "normalised_email" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_donation_intents_tenant_status",
                table: "pay_donation_intents",
                columns: new[] { "tenant_id", "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_donor",
                table: "pay_donations",
                column: "donor_id",
                filter: "donor_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_intent",
                table: "pay_donations",
                column: "donation_intent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_payment_attempt_id",
                table: "pay_donations",
                column: "payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_reference",
                table: "pay_donations",
                column: "donation_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_tenant_campaign",
                table: "pay_donations",
                columns: new[] { "tenant_id", "campaign_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_tenant_reconciliation",
                table: "pay_donations",
                columns: new[] { "tenant_id", "reconciliation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_donations_tenant_status_date",
                table: "pay_donations",
                columns: new[] { "tenant_id", "status", "donated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_pay_gateway_accounts_merchant",
                table: "pay_gateway_accounts",
                columns: new[] { "gateway_name", "merchant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pay_gateway_accounts_tenant_gateway",
                table: "pay_gateway_accounts",
                columns: new[] { "tenant_id", "gateway_name", "is_test_mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_attempts_gateway_reference",
                table: "pay_payment_attempts",
                column: "gateway_reference",
                unique: true,
                filter: "gateway_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_attempts_intent_number",
                table: "pay_payment_attempts",
                columns: new[] { "donation_intent_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_attempts_tenant_status",
                table: "pay_payment_attempts",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_events_gateway_event",
                table: "pay_payment_events",
                columns: new[] { "gateway_name", "gateway_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_events_gateway_reference",
                table: "pay_payment_events",
                column: "gateway_reference",
                filter: "gateway_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_events_payment_attempt_id",
                table: "pay_payment_events",
                column: "payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_payment_events_status_received",
                table: "pay_payment_events",
                columns: new[] { "status", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_receipt_deliveries_receipt",
                table: "pay_receipt_deliveries",
                columns: new[] { "receipt_id", "attempted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_receipt_deliveries_tenant_status",
                table: "pay_receipt_deliveries",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_pay_receipt_number_counters_scope",
                table: "pay_receipt_number_counters",
                columns: new[] { "tenant_id", "financial_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_receipts_donation",
                table: "pay_receipts",
                column: "donation_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_receipts_supersedes_receipt_id",
                table: "pay_receipts",
                column: "supersedes_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_receipts_tenant_delivery",
                table: "pay_receipts",
                columns: new[] { "tenant_id", "delivery_status" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_receipts_tenant_status",
                table: "pay_receipts",
                columns: new[] { "tenant_id", "status", "issued_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_pay_receipts_number",
                table: "pay_receipts",
                columns: new[] { "tenant_id", "financial_year", "receipt_number" },
                unique: true,
                filter: "receipt_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pay_refund_cases_requested_by",
                table: "pay_refund_cases",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pay_refund_cases_tenant_status",
                table: "pay_refund_cases",
                columns: new[] { "tenant_id", "status", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_pay_refund_cases_open_per_donation",
                table: "pay_refund_cases",
                column: "donation_id",
                unique: true,
                filter: "status = 'Requested'");

            migrationBuilder.CreateIndex(
                name: "ux_pay_refund_cases_reference",
                table: "pay_refund_cases",
                columns: new[] { "tenant_id", "case_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pay_audit_events");

            migrationBuilder.DropTable(
                name: "pay_chargeback_cases");

            migrationBuilder.DropTable(
                name: "pay_gateway_accounts");

            migrationBuilder.DropTable(
                name: "pay_payment_events");

            migrationBuilder.DropTable(
                name: "pay_receipt_deliveries");

            migrationBuilder.DropTable(
                name: "pay_receipt_number_counters");

            migrationBuilder.DropTable(
                name: "pay_refund_cases");

            migrationBuilder.DropTable(
                name: "pay_receipts");

            migrationBuilder.DropTable(
                name: "pay_donations");

            migrationBuilder.DropTable(
                name: "pay_payment_attempts");

            migrationBuilder.DropTable(
                name: "pay_donation_intents");
        }
    }
}
