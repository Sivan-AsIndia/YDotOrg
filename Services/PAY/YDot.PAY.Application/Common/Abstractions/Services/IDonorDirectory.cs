namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// The organisation-scoped donor lookup, and the donor and account creation that follows a
/// successful payment.
///
/// SECTION 26 IS THE WHOLE REASON THIS INTERFACE EXISTS. The existing-donor check is
/// "OrganisationId AND NormalisedEmail", never e-mail alone: the same person may give to two
/// charities on this platform and be a known donor to one and a stranger to the other.
///
/// WHY IT IS AN ABSTRACTION RATHER THAN A DIRECT QUERY. Donors live in DON and user accounts
/// live in IAM; PAY owns neither. It reads the donor table it shares a database with and calls
/// IAM for the account, and keeping both behind one interface means the handlers express the
/// BUSINESS steps of section 15 - find donor, create donor, create account, send invite - rather
/// than the mechanics of where each record lives.
/// </summary>
public interface IDonorDirectory
{
    /// <summary>
    /// Section 26: is this e-mail already a donor for THIS Organisation?
    ///
    /// Returns null when no donor matches, which is section 14's "continue without signing in"
    /// branch. A match is section 13's "sign in first" branch.
    /// </summary>
    Task<DonorMatch?> FindByEmailAsync(
        Guid tenantId, string normalisedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Section 15: creates the donor record from the intent after a successful payment.
    ///
    /// THE INTENT IS THE AUTHORITATIVE SOURCE for the donor's details, which the brief states
    /// explicitly - the donor typed them at the moment they gave, and nothing later is closer to
    /// the truth of who made that gift.
    /// </summary>
    Task<DonorMatch> CreateDonorAsync(
        CreateDonorFromIntentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Section 17: creates the donor's user account and sends the activation invitation.
    ///
    /// NO PASSWORD IS SET HERE. The account is created in an invited state and the donor chooses
    /// their own password through the activation link - the brief is explicit that the system
    /// should not need to know a password at this point.
    /// </summary>
    Task<DonorAccountResult> CreateAccountAndInviteAsync(
        CreateDonorAccountRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Section 16 and 28: marks the originating lead converted and links it to the donor.
    ///
    /// The conversion point is a successful payment, NOT the lead owner marking it qualified -
    /// which is the rule the brief states twice.
    /// </summary>
    Task MarkLeadConvertedAsync(
        Guid tenantId, Guid leadId, Guid donorId, CancellationToken cancellationToken);
}

/// <summary>A donor found or created for one Organisation.</summary>
public sealed record DonorMatch(
    Guid DonorId,
    string DisplayName,
    string Email,
    /// <summary>The donor's IAM user account, where one exists. Null before activation.</summary>
    Guid? UserId,
    bool HasActiveAccount);

/// <summary>Everything needed to create a donor from an intent. Section 15.</summary>
public sealed record CreateDonorFromIntentRequest(
    Guid TenantId,
    Guid BusinessUnitId,
    string Name,
    string Email,
    string NormalisedEmail,
    string? Mobile,
    string? TaxIdentifier,
    string? AddressLine1,
    string? AddressLine2,
    Guid? CountryId,
    Guid? StateId,
    Guid? CityId,
    string? PostalCode,
    Guid? OriginatingLeadId);

/// <summary>Everything needed to create the donor's account and invite them. Section 17.</summary>
public sealed record CreateDonorAccountRequest(
    Guid TenantId,
    Guid DonorId,
    string Name,
    string Email,
    string? Mobile,
    /// <summary>Named on the invitation so the donor recognises what they are activating.</summary>
    string DonationReference);

/// <summary>The result of creating an account, including whether the invitation went out.</summary>
public sealed record DonorAccountResult(
    Guid? UserId,
    bool AccountCreated,
    bool InvitationSent,
    /// <summary>
    /// Why it did not happen, when it did not.
    ///
    /// A FAILURE HERE MUST NOT FAIL THE DONATION. The money is already taken; an invitation that
    /// could not be sent is a follow-up task, not a reason to reject a gift that succeeded.
    /// </summary>
    string? FailureReason);
