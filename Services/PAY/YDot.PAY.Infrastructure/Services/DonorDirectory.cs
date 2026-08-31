using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Infrastructure.Identity;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// The organisation-scoped donor lookup, and the donor creation that follows a successful
/// payment.
///
/// IT READS AND WRITES DON'S TABLES DIRECTLY, over the database all four services share. That is
/// a deliberate decision with a real trade-off, so it is worth stating plainly.
///
/// WHY NOT AN HTTP CALL TO DON. Section 15 says the donor record is created as part of recording
/// a successful payment. If that step were an HTTP call it would sit inside the transaction that
/// writes the donation, and a DON outage - or a slow response, or a timeout that succeeded on the
/// far side - would either roll back a payment the gateway has already captured or leave a
/// donation with no donor. Money has already moved by this point; the write must be local and
/// atomic.
///
/// WHY NOT A PROJECT REFERENCE TO DON. That would make PAY depend on DON's domain assembly and
/// give it DON's whole model, migrations included - two services able to migrate the same tables
/// is worse than a narrow SQL seam.
///
/// SO THE SEAM IS THIS FILE, AND IT IS DELIBERATELY NARROW: four statements against three
/// columns sets. Every one is parameterised, every one is scoped by organisation_id, and if
/// donors ever move to their own database only this class changes.
///
/// EVERY LOOKUP IS "ORGANISATION AND E-MAIL", NEVER E-MAIL ALONE. Section 26 is explicit: the
/// same person may give to two charities on this platform and be a known donor to one and a
/// stranger to the other. A global e-mail match would tell charity A that a donor of charity B
/// exists, which is a disclosure in itself.
/// </summary>
public sealed class DonorDirectory(
    PaymentDbContext context,
    IIdentityAccountService accounts,
    ILogger<DonorDirectory> logger) : IDonorDirectory
{
    /// <summary>
    /// Section 26: is this e-mail already a donor for THIS Organisation?
    ///
    /// THE MATCH IS ON THE NORMALISED COLUMN, which DON maintains as its duplicate-detection key
    /// and PAY computes the same way - lower-cased and trimmed. Matching on the display column
    /// would miss "Jo@Example.org" against "jo@example.org" and create a second donor record for
    /// somebody who is already known.
    ///
    /// The primary e-mail is checked as well as the business key, because a donor created through
    /// DON's own screens may have been keyed on a phone number instead.
    /// </summary>
    public async Task<DonorMatch?> FindByEmailAsync(
        Guid tenantId, string normalisedEmail, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(normalisedEmail))
        {
            return null;
        }

        const string Sql = """
            SELECT id, donor_type, first_name, last_name, organisation_name, primary_email, status
            FROM don_donors
            WHERE organisation_id = @organisation_id
              AND (LOWER(primary_email) = @email OR normalized_business_key = @email)
              AND merged_into_donor_id IS NULL
            ORDER BY created_at_utc
            LIMIT 1
            """;

        await using var command = await CreateCommandAsync(Sql, cancellationToken);

        command.Parameters.Add(new NpgsqlParameter("organisation_id", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = normalisedEmail });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var donorId = reader.GetGuid(0);
        var donorType = reader.GetString(1);
        var firstName = reader.IsDBNull(2) ? null : reader.GetString(2);
        var lastName = reader.IsDBNull(3) ? null : reader.GetString(3);
        var organisationName = reader.IsDBNull(4) ? null : reader.GetString(4);
        var email = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);

        await reader.CloseAsync();

        // The account lives in IAM, not DON, so it is a separate question - and the answer
        // decides whether the donor is sent to sign in (section 13) or straight on to payment
        // (section 14).
        var account = await accounts.FindDonorAccountAsync(tenantId, normalisedEmail, cancellationToken);

        return new DonorMatch(
            donorId,
            BuildDisplayName(donorType, firstName, lastName, organisationName),
            email,
            account?.UserId,
            account?.IsActive ?? false);
    }

    /// <summary>
    /// Section 15: creates the donor record from the intent after a successful payment.
    ///
    /// THE INTENT IS THE AUTHORITATIVE SOURCE for the donor's details, which the brief states
    /// explicitly - the donor typed them at the moment they gave, and nothing later is closer to
    /// the truth of who made that gift.
    ///
    /// THE DONOR NUMBER IS ALLOCATED FROM THE EXISTING SEQUENCE, by taking the highest number
    /// already issued to this organisation and adding one, inside the caller's transaction. DON's
    /// unique index on (organisation_id, donor_number) is the backstop: two donors created in the
    /// same instant cannot both take the number, and the loser's insert fails rather than
    /// producing a duplicate.
    ///
    /// THE DONOR IS CREATED ACTIVE AND APPROVED, unlike one keyed in by a fundraiser. That is the
    /// correct difference: a donation that has actually been captured is stronger evidence of a
    /// real donor than any manual review, and holding the record pending would leave a paid
    /// donation attached to an unapproved donor.
    /// </summary>
    public async Task<DonorMatch> CreateDonorAsync(
        CreateDonorFromIntentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var donorId = Guid.NewGuid();
        var donorNumber = await AllocateDonorNumberAsync(request.TenantId, cancellationToken);
        var (firstName, lastName) = SplitName(request.Name);

        const string Sql = """
            INSERT INTO don_donors (
                id, organisation_id, donor_number, donor_type,
                first_name, last_name, organisation_name,
                primary_email, primary_phone, preferred_language,
                status, do_not_contact, approval_state,
                relationship_owner_user_id, relationship_owner_name,
                source_lead_id, merged_into_donor_id, normalized_business_key,
                submitted_at_utc, approved_at_utc, approved_by_user_id,
                cancellation_reason, archive_reason, notes,
                created_at_utc, created_by_user_id, updated_at_utc, updated_by_user_id, version)
            VALUES (
                @id, @organisation_id, @donor_number, 'Individual',
                @first_name, @last_name, NULL,
                @primary_email, @primary_phone, 'en-IN',
                'Active', false, 'Approved',
                NULL, NULL,
                @source_lead_id, NULL, @normalized_business_key,
                @now, @now, NULL,
                NULL, NULL, @notes,
                @now, @created_by, NULL, NULL, 1)
            """;

        var now = DateTimeOffset.UtcNow;

        await using var command = await CreateCommandAsync(Sql, cancellationToken);

        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = donorId });
        command.Parameters.Add(new NpgsqlParameter("organisation_id", NpgsqlDbType.Uuid) { Value = request.TenantId });
        command.Parameters.Add(new NpgsqlParameter("donor_number", NpgsqlDbType.Text) { Value = donorNumber });
        command.Parameters.Add(new NpgsqlParameter("first_name", NpgsqlDbType.Text) { Value = firstName });
        command.Parameters.Add(new NpgsqlParameter("last_name", NpgsqlDbType.Text)
        {
            Value = (object?)lastName ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("primary_email", NpgsqlDbType.Text) { Value = request.Email });
        command.Parameters.Add(new NpgsqlParameter("primary_phone", NpgsqlDbType.Text)
        {
            Value = (object?)request.Mobile ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("source_lead_id", NpgsqlDbType.Uuid)
        {
            Value = (object?)request.OriginatingLeadId ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("normalized_business_key", NpgsqlDbType.Text)
        {
            Value = request.NormalisedEmail
        });
        command.Parameters.Add(new NpgsqlParameter("notes", NpgsqlDbType.Text)
        {
            Value = "Created automatically from a completed donation."
        });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });

        // Guid.Empty rather than null: the donor record was created by the system on behalf of an
        // anonymous donor, and DON's column does not permit null.
        command.Parameters.Add(new NpgsqlParameter("created_by", NpgsqlDbType.Uuid) { Value = Guid.Empty });

        await command.ExecuteNonQueryAsync(cancellationToken);

        logger.LogInformation(
            "Created donor {DonorId} ({DonorNumber}) for organisation {TenantId} from a completed donation.",
            donorId,
            donorNumber,
            request.TenantId);

        return new DonorMatch(
            donorId,
            BuildDisplayName("Individual", firstName, lastName, null),
            request.Email,
            UserId: null,
            HasActiveAccount: false);
    }

    /// <summary>
    /// Section 17: creates the donor's user account and sends the activation invitation.
    ///
    /// NO PASSWORD IS SET. The account is created in an invited state and the donor chooses their
    /// own password through the activation link - the brief is explicit that the system should
    /// not need to know one at this point.
    ///
    /// A FAILURE HERE RETURNS A RESULT RATHER THAN THROWING, and the caller is written to
    /// continue. The money is already taken; an invitation that could not be sent is a follow-up
    /// task, not a reason to reject a gift that succeeded.
    /// </summary>
    public Task<DonorAccountResult> CreateAccountAndInviteAsync(
        CreateDonorAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return accounts.CreateDonorAccountAndInviteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sections 16 and 28: marks the originating lead converted and links it to the donor.
    ///
    /// THE CONVERSION POINT IS A SUCCESSFUL PAYMENT, not the lead owner marking it qualified -
    /// the rule the brief states twice. A lead already converted is left exactly as it was: the
    /// first conversion is the one that counts, and overwriting it would move the conversion date
    /// on to the donor's second gift.
    /// </summary>
    public async Task MarkLeadConvertedAsync(
        Guid tenantId, Guid leadId, Guid donorId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || leadId == Guid.Empty)
        {
            return;
        }

        const string Sql = """
            UPDATE don_leads
            SET status = 'Converted',
                converted_donor_id = @donor_id,
                converted_at_utc = @now,
                updated_at_utc = @now,
                version = version + 1
            WHERE id = @lead_id
              AND organisation_id = @organisation_id
              AND converted_donor_id IS NULL
            """;

        await using var command = await CreateCommandAsync(Sql, cancellationToken);

        command.Parameters.Add(new NpgsqlParameter("donor_id", NpgsqlDbType.Uuid) { Value = donorId });
        command.Parameters.Add(new NpgsqlParameter("lead_id", NpgsqlDbType.Uuid) { Value = leadId });
        command.Parameters.Add(new NpgsqlParameter("organisation_id", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz)
        {
            Value = DateTimeOffset.UtcNow
        });

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);

        if (affected == 0)
        {
            // Not an error. Either the lead belongs to another organisation - in which case doing
            // nothing is exactly right - or it was already converted by an earlier gift.
            logger.LogInformation(
                "Lead {LeadId} was not marked converted: it is already converted or belongs to "
                + "another organisation.",
                leadId);
        }
    }

    // =====================================================================================
    // Internals
    // =====================================================================================

    /// <summary>
    /// The next donor number for an organisation, as DON-2026-000001.
    ///
    /// IT READS THE MAXIMUM RATHER THAN A COUNTER, because DON owns the numbering and adding a
    /// counter table from this side would give two services two different ideas of the next
    /// number. The unique index on (organisation_id, donor_number) makes a collision an error
    /// rather than a duplicate, and this runs inside the caller's transaction so a concurrent
    /// insert is serialised by that index rather than silently overwriting.
    /// </summary>
    private async Task<string> AllocateDonorNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        var prefix = $"DON-{year}-";

        const string Sql = """
            SELECT donor_number
            FROM don_donors
            WHERE organisation_id = @organisation_id AND donor_number LIKE @prefix
            ORDER BY donor_number DESC
            LIMIT 1
            """;

        await using var command = await CreateCommandAsync(Sql, cancellationToken);

        command.Parameters.Add(new NpgsqlParameter("organisation_id", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("prefix", NpgsqlDbType.Text) { Value = prefix + "%" });

        var highest = await command.ExecuteScalarAsync(cancellationToken) as string;

        var next = 1;

        if (!string.IsNullOrWhiteSpace(highest)
            && int.TryParse(
                highest[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            next = parsed + 1;
        }

        return prefix + next.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A command on the SAME connection and transaction the caller is already using.
    ///
    /// THAT IS THE WHOLE REASON THIS CLASS TAKES A DbContext rather than a connection string. A
    /// separate connection would open a separate transaction, and the donor would commit
    /// independently of the donation - so a rolled-back donation would leave a donor record for a
    /// gift that never happened.
    /// </summary>
    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = new NpgsqlCommand(sql, connection);

        if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    /// <summary>
    /// Splits a typed name into first and last.
    ///
    /// CRUDE ON PURPOSE. The donor typed one box, and a clever split - honorifics, particles,
    /// multi-word surnames - gets more names wrong than it gets right. Everything before the last
    /// space is the first name, which keeps "Maria del Carmen Rodriguez" intact rather than
    /// mangled. DON's display logic joins them back together for most purposes anyway.
    /// </summary>
    private static (string FirstName, string? LastName) SplitName(string name)
    {
        var trimmed = name.Trim();

        var lastSpace = trimmed.LastIndexOf(' ');

        return lastSpace <= 0
            ? (trimmed, null)
            : (trimmed[..lastSpace], trimmed[(lastSpace + 1)..]);
    }

    private static string BuildDisplayName(
        string donorType, string? firstName, string? lastName, string? organisationName)
    {
        if (string.Equals(donorType, "Organisation", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(organisationName))
        {
            return organisationName;
        }

        var joined = string.Join(' ', new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(joined) ? "Donor" : joined;
    }
}
