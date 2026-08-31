using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YDots.DON.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDonorSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "don_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "don_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "don_donors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_number = table.Column<string>(type: "text", nullable: false),
                    donor_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    organisation_name = table.Column<string>(type: "text", nullable: true),
                    primary_email = table.Column<string>(type: "text", nullable: true),
                    primary_phone = table.Column<string>(type: "text", nullable: true),
                    preferred_language = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    do_not_contact = table.Column<bool>(type: "boolean", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    relationship_owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relationship_owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merged_into_donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    normalized_business_key = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    archive_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "don_idempotency_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_idempotency_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "don_outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "don_leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mobile_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    preferred_language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    city = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    geography_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consent_state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    consent_evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    preferred_contact_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duplicate_candidate_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    team_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    next_action = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    next_action_due_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sla_state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    last_contact_outcome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    last_contacted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    qualified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    converted_donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_draft = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_leads_don_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "don_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    consent_state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    notice_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    evidence_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    effective_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expiry_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    public_recognition_preference = table.Column<bool>(type: "boolean", nullable: false),
                    contact_restrictions = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    correction_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    superseded_by_consent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawal_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    captured_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_consents_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_contacts_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    classification = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    size_in_bytes = table.Column<long>(type: "bigint", nullable: true),
                    scan_status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_documents_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_donation_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    as_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refreshed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_donation_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_donation_summaries_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_identity_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verification_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verification_purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    verification_channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    masked_destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    expiry_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    identity_confidence = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    challenge_code_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_identity_verifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_identity_verifications_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_interactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interaction_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_interactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_interactions_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_merge_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_a_donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_b_donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_comparison = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    identity_confidence = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    matching_evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conflicting_fields = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    donation_history_impact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    consent_impact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decision = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    surviving_donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merge_preview = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_merge_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_merge_cases_don_donors_candidate_a_donor_id",
                        column: x => x.candidate_a_donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_donor_merge_cases_don_donors_candidate_b_donor_id",
                        column: x => x.candidate_b_donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_promises",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    promised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_promises", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_promises_don_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "don_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_donor_promises_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_donor_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_donor_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_donor_tags_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "don_follow_up_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    follow_up_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    donor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relationship_owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    permitted_channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    preferred_language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    preferred_contact_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_action = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    priority = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    consent_warning_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    consent_notice_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    consent_acknowledged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completion_outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reschedule_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_follow_up_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_follow_up_tasks_don_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "don_donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_follow_up_tasks_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "don_leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_lead_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    new_owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    assignment_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    effective_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_bulk_route = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_lead_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_don_lead_assignments_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "don_leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_don_audit_events_correlation",
                table: "don_audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_audit_events_org_created",
                table: "don_audit_events",
                columns: new[] { "organisation_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_audit_events_target",
                table: "don_audit_events",
                columns: new[] { "target_type", "target_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_campaigns_org_code",
                table: "don_campaigns",
                columns: new[] { "organisation_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_consents_donor_channel_status",
                table: "don_consents",
                columns: new[] { "donor_id", "channel", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_don_consents_effective",
                table: "don_consents",
                column: "effective_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_don_consents_lead",
                table: "don_consents",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_contacts_donor",
                table: "don_donor_contacts",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_documents_donor",
                table: "don_donor_documents",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donation_summaries_donor_stage",
                table: "don_donor_donation_summaries",
                columns: new[] { "donor_id", "stage", "currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_verifications_donor_status",
                table: "don_donor_identity_verifications",
                columns: new[] { "donor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_don_verifications_reference",
                table: "don_donor_identity_verifications",
                column: "verification_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_interactions_donor_occurred",
                table: "don_donor_interactions",
                columns: new[] { "donor_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_interactions_lead",
                table: "don_donor_interactions",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_merge_cases_candidate_b_donor_id",
                table: "don_donor_merge_cases",
                column: "candidate_b_donor_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_merge_cases_pair",
                table: "don_donor_merge_cases",
                columns: new[] { "candidate_a_donor_id", "candidate_b_donor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_don_merge_cases_reference",
                table: "don_donor_merge_cases",
                column: "review_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_merge_cases_status_updated",
                table: "don_donor_merge_cases",
                columns: new[] { "status", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_promises_campaign_id",
                table: "don_donor_promises",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_promises_donor",
                table: "don_donor_promises",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_promises_reference",
                table: "don_donor_promises",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_donor_tags_donor_code",
                table: "don_donor_tags",
                columns: new[] { "donor_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_donors_org_business_key",
                table: "don_donors",
                columns: new[] { "organisation_id", "normalized_business_key" });

            migrationBuilder.CreateIndex(
                name: "ix_don_donors_org_donor_number",
                table: "don_donors",
                columns: new[] { "organisation_id", "donor_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_donors_owner",
                table: "don_donors",
                column: "relationship_owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_donors_status_updated",
                table: "don_donors",
                columns: new[] { "status", "updated_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_don_follow_up_tasks_donor_id",
                table: "don_follow_up_tasks",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_follow_up_tasks_lead_id",
                table: "don_follow_up_tasks",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_follow_ups_owner",
                table: "don_follow_up_tasks",
                column: "relationship_owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_follow_ups_reference",
                table: "don_follow_up_tasks",
                column: "follow_up_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_follow_ups_status_due",
                table: "don_follow_up_tasks",
                columns: new[] { "status", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_idempotency_key_endpoint",
                table: "don_idempotency_keys",
                columns: new[] { "organisation_id", "key", "endpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_lead_assignments_lead_effective",
                table: "don_lead_assignments",
                columns: new[] { "lead_id", "effective_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_lead_assignments_owner",
                table: "don_lead_assignments",
                column: "new_owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_leads_campaign",
                table: "don_leads",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_leads_email",
                table: "don_leads",
                column: "email_address");

            migrationBuilder.CreateIndex(
                name: "ix_don_leads_mobile",
                table: "don_leads",
                column: "mobile_number");

            migrationBuilder.CreateIndex(
                name: "ix_don_leads_org_reference",
                table: "don_leads",
                columns: new[] { "organisation_id", "lead_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_leads_owner",
                table: "don_leads",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_leads_status_due",
                table: "don_leads",
                columns: new[] { "status", "next_action_due_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_don_outbox_aggregate",
                table: "don_outbox_messages",
                columns: new[] { "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_don_outbox_unprocessed",
                table: "don_outbox_messages",
                columns: new[] { "processed_at_utc", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "don_audit_events");

            migrationBuilder.DropTable(
                name: "don_consents");

            migrationBuilder.DropTable(
                name: "don_donor_contacts");

            migrationBuilder.DropTable(
                name: "don_donor_documents");

            migrationBuilder.DropTable(
                name: "don_donor_donation_summaries");

            migrationBuilder.DropTable(
                name: "don_donor_identity_verifications");

            migrationBuilder.DropTable(
                name: "don_donor_interactions");

            migrationBuilder.DropTable(
                name: "don_donor_merge_cases");

            migrationBuilder.DropTable(
                name: "don_donor_promises");

            migrationBuilder.DropTable(
                name: "don_donor_tags");

            migrationBuilder.DropTable(
                name: "don_follow_up_tasks");

            migrationBuilder.DropTable(
                name: "don_idempotency_keys");

            migrationBuilder.DropTable(
                name: "don_lead_assignments");

            migrationBuilder.DropTable(
                name: "don_outbox_messages");

            migrationBuilder.DropTable(
                name: "don_donors");

            migrationBuilder.DropTable(
                name: "don_leads");

            migrationBuilder.DropTable(
                name: "don_campaigns");
        }
    }
}
