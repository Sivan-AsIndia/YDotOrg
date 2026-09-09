using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Identity;

/// <summary>
/// Talks to IAM about donor accounts: a direct read for the existing-account check, and an HTTP
/// call for creation. See <see cref="IIdentityAccountService"/> for why the two differ.
/// </summary>
public sealed class IdentityAccountService(
    PaymentDbContext context,
    IHttpClientFactory httpClientFactory,
    IOptions<IdentityIntegrationSettings> settings,
    ITenantHostDirectory tenantHosts,
    ILogger<IdentityAccountService> logger) : IIdentityAccountService
{
    /// <summary>The name the client is registered under in DependencyInjection.</summary>
    internal const string HttpClientName = "iam";

    private readonly IdentityIntegrationSettings _settings = settings.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Whether this e-mail already has an account in this Organisation.
    ///
    /// IT READS <c>normalized_email</c>, which is the column IAM's own uniqueness index uses, so
    /// this check agrees with what IAM would decide rather than approximating it. The status is
    /// returned as well as the id because "an account exists but is suspended" is a different
    /// answer from "an account exists and works" - and section 13 should not send a donor to sign
    /// in to something they cannot get into.
    /// </summary>
    public async Task<DonorAccountSummary?> FindDonorAccountAsync(
        Guid tenantId, string normalisedEmail, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(normalisedEmail))
        {
            return null;
        }

        const string Sql = """
            SELECT id, status
            FROM iam_users
            WHERE tenant_id = @tenant_id AND normalized_email = @normalized_email
            LIMIT 1
            """;

        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(Sql, connection);

            if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
            {
                command.Transaction = transaction;
            }

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });

            // IAM normalises to UPPER, ASP.NET Identity's convention. PAY normalises to lower for
            // its own columns, so the two are reconciled here rather than at forty call sites.
            command.Parameters.Add(new NpgsqlParameter("normalized_email", NpgsqlDbType.Text)
            {
                Value = normalisedEmail.ToUpperInvariant()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var userId = reader.GetGuid(0);
            var status = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1);

            return new DonorAccountSummary(
                userId,
                string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase),
                status);
        }
        catch (NpgsqlException exception)
        {
            // NOT RETHROWN. This runs on the public donation path, and the consequence of failing
            // to answer is that the donor is offered "continue without signing in" - which is a
            // worse experience than being recognised, but a working one. Throwing would stop the
            // donation outright over a lookup that is only an optimisation.
            logger.LogError(
                exception,
                "Could not check for an existing donor account in organisation {TenantId}. "
                + "Treating the donor as new.",
                tenantId);

            return null;
        }
    }

    /// <summary>
    /// Creates the donor's account in IAM and asks it to send the activation invitation.
    ///
    /// EVERY FAILURE PATH RETURNS A RESULT, never an exception - see the interface for why. The
    /// three distinct outcomes are all reported honestly:
    ///
    ///   * integration switched off: no account, no failure, nothing went wrong;
    ///   * IAM refused (duplicate address, missing permission): no account, with the reason;
    ///   * IAM unreachable: no account, with the reason, and a logged error.
    ///
    /// The caller records the outcome against the donation, so an operator can see afterwards
    /// which donors still need an invitation.
    /// </summary>
    public async Task<DonorAccountResult> CreateDonorAccountAndInviteAsync(
        CreateDonorAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return new DonorAccountResult(
                UserId: null,
                AccountCreated: false,
                InvitationSent: false,
                FailureReason: "Donor portal accounts are not enabled for this installation.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            // ============================================================================
            // THE HOST HEADER IS THE ORGANISATION, AND WITHOUT IT NOTHING HERE WORKS.
            // ============================================================================
            // IAM resolves which Organisation a request belongs to from the Host header. This
            // client's BaseAddress is the container address - http://ydot-iam:8080 - so every
            // call arrived at IAM saying Host: ydot-iam:8080, which is registered to no
            // Organisation at all. IAM therefore looked the service account up in nothing, found
            // nothing, and answered INVALID_CREDENTIALS: "The sign-in details are incorrect."
            //
            // The credentials were correct the whole time. The same username and password
            // succeed against Host: ten1.localhost and fail against Host: ydot-iam:8080, which
            // is why this looked like a configuration problem nobody could find - and why donor
            // portal accounts were never created for anyone, on any donation.
            //
            // The header is set rather than the address changed: the connection must still go to
            // the container, and only the name IAM reads changes. It is the same fact the
            // receipt link resolves, from the same table.
            var tenantHost = await tenantHosts.GetPrimaryHostAsync(request.TenantId, cancellationToken);

            if (!string.IsNullOrWhiteSpace(tenantHost))
            {
                client.DefaultRequestHeaders.Host = tenantHost;
            }
            else
            {
                logger.LogWarning(
                    "No host is registered for organisation {TenantId}, so the identity service "
                    + "cannot resolve it and the donor account for donation {DonationReference} "
                    + "will not be created.",
                    request.TenantId,
                    request.DonationReference);
            }

            var token = await SignInAsync(client, cancellationToken);

            if (token is null)
            {
                return new DonorAccountResult(
                    null, false, false,
                    "Could not authenticate with the identity service to create the donor account.");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var (firstName, lastName) = SplitName(request.Name);
            var (mobileCountryCode, mobileNumber) = SplitMobile(request.Mobile);

            var payload = new CreateUserPayload(
                FirstName: firstName,
                LastName: lastName,
                Email: request.Email,
                MobileCountryCode: mobileCountryCode,
                MobileNumber: mobileNumber,
                AccountCategory: _settings.DonorAccountCategory,

                // The invitation names the donation so the donor recognises what they are
                // activating. An unexplained account invitation from a charity is one people
                // report as phishing.
                InvitationMessage:
                    $"Thank you for your donation ({request.DonationReference}). "
                    + "Activate your account to see your giving history and download your receipts.",

                SendInvitation: true);

            using var response = await client.PostAsJsonAsync(
                "api/v1/users", payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogWarning(
                    "The identity service refused to create a donor account for donation "
                    + "{DonationReference}: {StatusCode} {Body}",
                    request.DonationReference,
                    (int)response.StatusCode,
                    Truncate(body));

                return new DonorAccountResult(
                    null, false, false,
                    $"The identity service refused the account ({(int)response.StatusCode}).");
            }

            var created = await response.Content.ReadFromJsonAsync<ApiEnvelope<CreateUserResult>>(
                JsonOptions, cancellationToken);

            var userId = created?.Data?.Id;

            logger.LogInformation(
                "Created donor account {UserId} for donation {DonationReference}.",
                userId,
                request.DonationReference);

            return new DonorAccountResult(
                userId,
                AccountCreated: userId.HasValue,
                InvitationSent: userId.HasValue,
                FailureReason: userId.HasValue ? null : "The identity service returned no account id.");
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Could not reach the identity service to create a donor account for donation "
                + "{DonationReference}. The donation itself is unaffected.",
                request.DonationReference);

            return new DonorAccountResult(
                null, false, false, "The identity service could not be reached.");
        }
    }

    /// <summary>
    /// Signs in as the service account and returns the access token.
    ///
    /// THE TOKEN IS NOT CACHED. This runs once per new donor - rare enough that a cache would
    /// save little - and caching a bearer token in a long-lived object means holding a
    /// credential in memory across requests for no measurable gain.
    /// </summary>
    private async Task<string?> SignInAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ServiceAccountUsername)
            || string.IsNullOrWhiteSpace(_settings.ServiceAccountPassword))
        {
            logger.LogWarning(
                "Donor account creation is enabled but no service account credential is "
                + "configured. Donors will be created without portal accounts.");

            return null;
        }

        using var response = await client.PostAsJsonAsync(
            "api/v1/users/sign-in",
            new SignInPayload(_settings.ServiceAccountUsername, _settings.ServiceAccountPassword),
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // THE BODY IS LOGGED, NOT JUST THE STATUS CODE. A bare "rejected: 400" is what hid
            // the wrong field name for as long as it did - IAM says exactly which field it could
            // not read, and that sentence is the whole diagnosis.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogWarning(
                "The identity service rejected the PAY service account sign-in as {Username} on "
                + "host {Host}: {StatusCode} {Body}",
                _settings.ServiceAccountUsername,
                client.DefaultRequestHeaders.Host ?? client.BaseAddress?.Host,
                (int)response.StatusCode,
                Truncate(body));

            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SignInResult>>(
            JsonOptions, cancellationToken);

        return envelope?.Data?.AccessToken;
    }

    private static (string FirstName, string LastName) SplitName(string name)
    {
        var trimmed = name.Trim();
        var lastSpace = trimmed.LastIndexOf(' ');

        // IAM requires both parts, so a single-word name repeats itself rather than being
        // rejected. A donor called "Prince" must still be able to have an account.
        return lastSpace <= 0
            ? (trimmed, trimmed)
            : (trimmed[..lastSpace], trimmed[(lastSpace + 1)..]);
    }

    /// <summary>
    /// Splits a donor's mobile number into the country code and the number IAM asks for.
    ///
    /// A NUMBER THAT WILL NOT PARSE IS DROPPED, NOT SENT. IAM validates the whole request, so an
    /// unusable phone number does not fail the phone number - it fails the CREATE, and the donor
    /// loses the account and the activation invitation over a field that is optional on the form
    /// they filled in. A donor record with no telephone number is a small loss; a donor with no
    /// way into the portal they were promised is the thing this method exists to prevent.
    ///
    /// A DONOR'S OWN "+" CODE WINS over the configured default: somebody who typed +44 is not in
    /// India, and stamping +91 on their number would store a number that dials nobody.
    /// </summary>
    private (string? CountryCode, string? Number) SplitMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return (null, null);
        }

        var trimmed = mobile.Trim();
        string countryCode;
        string digits;

        if (trimmed.StartsWith('+'))
        {
            // Everything up to the first separator is the code; failing a separator, the E.164
            // maximum of three digits, which is all a dialling code can be.
            var rest = new string([.. trimmed[1..].Where(character => char.IsDigit(character) || character == ' ' || character == '-')]);
            var parts = rest.Split([' ', '-'], 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2)
            {
                countryCode = "+" + new string([.. parts[0].Where(char.IsDigit)]);
                digits = new string([.. parts[1].Where(char.IsDigit)]);
            }
            else
            {
                var all = new string([.. trimmed.Where(char.IsDigit)]);

                // Longer than a national number means a code is glued to the front of it.
                countryCode = all.Length > 10 ? "+" + all[..(all.Length - 10)] : "+" + all;
                digits = all.Length > 10 ? all[(all.Length - 10)..] : string.Empty;
            }
        }
        else
        {
            countryCode = _settings.DefaultMobileCountryCode?.Trim() ?? string.Empty;

            if (countryCode.Length > 0 && !countryCode.StartsWith('+'))
            {
                countryCode = "+" + countryCode;
            }

            digits = new string([.. trimmed.Where(char.IsDigit)]);
        }

        // IAM's own rule, applied here so a number it would reject never reaches it: a dialling
        // code of one to four digits, a national number of four to fourteen, fifteen in total.
        var codeDigits = countryCode.Length - 1;

        var usable = codeDigits is >= 1 and <= 4
                     && digits.Length is >= 4 and <= 14
                     && codeDigits + digits.Length <= 15;

        if (!usable)
        {
            logger.LogInformation(
                "A donor's mobile number could not be expressed with a country code and was "
                + "omitted from their account. The account is still created.");

            return (null, null);
        }

        return (countryCode, digits);
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    // ---- Wire shapes -------------------------------------------------------------------

    /// <summary>IAM's six-key response envelope, of which PAY needs one field.</summary>
    private sealed record ApiEnvelope<T>(bool Success, T? Data, string? Message);

    /// <summary>
    /// IAM's sign-in body.
    ///
    /// THE FIELD IS "Identifier", AND IT WAS "UsernameOrEmail" HERE. IAM's SignInRequest has no
    /// such property, so its validator rejected every call before the handler ran - 400,
    /// "The Identifier field is required." SignInAsync then returned null, and the only thing
    /// that reported it was a LogWarning saying the identity service "rejected the sign-in",
    /// which reads like a bad password rather than a body IAM could not even read.
    ///
    /// The consequence was silent and complete: no service-account token, so no donor account and
    /// no activation invitation, for every donation ever taken. The donation itself was recorded
    /// correctly, which is exactly why nobody noticed.
    /// </summary>
    private sealed record SignInPayload(string Identifier, string Password);

    private sealed record SignInResult(string? AccessToken);

    private sealed record CreateUserPayload(
        string FirstName,
        string LastName,
        string Email,
        string? MobileCountryCode,
        string? MobileNumber,
        string AccountCategory,
        string InvitationMessage,
        bool SendInvitation);

    private sealed record CreateUserResult(Guid Id);
}
